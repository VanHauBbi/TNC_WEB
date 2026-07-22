using System;
using System.Linq;
using System.Web.Mvc;
using WebBanHang.Models;
using System.Data.Entity;
using Microsoft.AspNet.SignalR;
using WebBanHang.Hubs;

namespace WebBanHang.Areas.Admin.Controllers
{
    // Bỏ comment dòng dưới nếu hệ thống Admin của bạn có yêu cầu đăng nhập
    // [Authorize(Roles = "A")] 
    public class AdminSupportController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // 1. Màn hình danh sách khách hàng đang chờ hỗ trợ
        public ActionResult Index()
        {
            // Lấy toàn bộ danh sách, không dùng .Where()
            var sessions = db.SupportSessions
                             .Include(s => s.Customer)
                             .OrderByDescending(s => s.StartTime)
                             .ToList();
            return View(sessions); // Trả về danh sách này cho View
        }

        // 2. Màn hình khung chat trực tiếp của Admin với Khách
        //public ActionResult ChatRoom(int? id)
        //{
        //    // 1. Kiểm tra chốt chặn: Nếu không có ID thì đá về trang danh sách
        //    if (id == null)
        //    {
        //        return RedirectToAction("Index");
        //    }

        //    // 2. Tìm phòng chat dựa vào ID
        //    var session = db.SupportSessions
        //                    .Include(s => s.SupportMessages)
        //                    .FirstOrDefault(s => s.SessionID == id);

        //    // 3. Nếu gõ bậy ID không có trong Database
        //    if (session == null)
        //    {
        //        return HttpNotFound("Không tìm thấy phiên hỗ trợ này.");
        //    }

        //    // 4. Nếu khách đang trạng thái "Đang chờ" (0), khi admin click vào sẽ tự đổi thành "Đang chat" (1)
        //    if (session.Status == 0)
        //    {
        //        session.Status = 1;
        //        db.SaveChanges();
        //    }

        //    return View(session);
        //}

        public ActionResult ChatRoom(int? id)
        {
            // 1. Kiểm tra chốt chặn: Nếu không có ID thì đá về trang danh sách
            if (id == null)
            {
                return RedirectToAction("Index");
            }

            // 2. Tìm phòng chat dựa vào ID (Bổ sung Include Customer để lấy tên khách hàng)
            var session = db.SupportSessions
                            .Include(s => s.SupportMessages)
                            .Include(s => s.Customer)
                            .FirstOrDefault(s => s.SessionID == id);

            // 3. Nếu gõ bậy ID không có trong Database
            if (session == null)
            {
                return HttpNotFound("Không tìm thấy phiên hỗ trợ này.");
            }

            // 4. Nếu khách đang trạng thái "Đang chờ" (0), khi admin click vào sẽ tự đổi thành "Đang chat" (1)
            if (session.Status == 0)
            {
                session.Status = 1;
                db.SaveChanges();
            }

            // 5. BỔ SUNG LẤY DANH SÁCH PHIÊN ĐỂ ĐỔ RA CỘT BÊN TRÁI
            ViewBag.ActiveSessions = db.SupportSessions
                                       .Include(s => s.Customer)
                                       .OrderByDescending(s => s.StartTime)
                                       .ToList();

            return View(session);
        }

        // 3. Nút bấm kết thúc cuộc hội thoại
        public ActionResult EndSession(int id)
        {
            var session = db.SupportSessions.Find(id);
            if (session != null)
            {
                // 1. Chuyển trạng thái sang Chờ kết thúc
                session.Status = 3;
                db.SaveChanges();

                // 2. Phát tín hiệu SignalR cho khách hàng
                var context = GlobalHost.ConnectionManager.GetHubContext<ChatHub>();

                // --- SỬA Ở DÒNG NÀY: Phải có chữ "Session_" ghép với id ---
                context.Clients.Group("Session_" + id).askForConfirmation();
            }
            return RedirectToAction("Index");
        }

        public ActionResult ViewHistoryDetail(int? id)
        {
            // Tìm phiên chat và Include luôn cả bảng SupportMessages (Tin nhắn) + Customer (Khách hàng)
            var session = db.SupportSessions
                            .Include(s => s.SupportMessages)
                            .Include(s => s.Customer)
                            .FirstOrDefault(s => s.SessionID == id);

            if (session == null)
            {
                return HttpNotFound("Không tìm thấy lịch sử phiên hỗ trợ này.");
            }

            return View(session);
        }
    }
}