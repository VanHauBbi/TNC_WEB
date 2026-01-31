using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using WebBanHang.Models;

namespace WebBanHang.Areas.Admin.Controllers
{
    public class OrdersController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // GET: Admin/Orders
        public ActionResult Index()
        {
            var orders = db.Orders
                        .Include("Customer") 
                        .ToList();
            return View(orders);
        }

        // GET: Admin/Orders/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var order = db.Orders.Find(id);
            if (order == null)
                return HttpNotFound();

            return View(order);
        }

        // POST: Admin/Orders/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    // 1. Tìm đơn hàng
                    var order = db.Orders
                                  .Include(o => o.OrderDetails) // RẤT QUAN TRỌNG: Include OrderDetails
                                  .SingleOrDefault(o => o.OrderID == id);

                    if (order == null)
                    {
                        TempData["ErrorMessage"] = "Không tìm thấy đơn hàng cần xóa.";
                        return RedirectToAction("Index");
                    }

                    // 2. XÓA TẤT CẢ CHI TIẾT ĐƠN HÀNG TRƯỚC (Giải quyết ràng buộc FK)
                    // Lấy danh sách chi tiết và xóa từng cái một
                    db.OrderDetails.RemoveRange(order.OrderDetails);

                    // 3. XÓA ĐƠN HÀNG CHÍNH
                    db.Orders.Remove(order);

                    db.SaveChanges();
                    transaction.Commit(); // Hoàn tất giao dịch

                    TempData["SuccessMessage"] = $"✅ Đã xóa hoàn toàn đơn hàng #{id} khỏi hệ thống.";
                }
                catch (Exception ex)
                {
                    transaction.Rollback(); // Hoàn lại nếu có lỗi
                    TempData["ErrorMessage"] = "❌ Lỗi xóa: Không thể xóa đơn hàng. Lỗi chi tiết: " + ex.Message;
                }
            }

            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }

        public ActionResult Process(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Order order = db.Orders.Include(o => o.Customer).SingleOrDefault(o => o.OrderID == id);
            if (order == null)
            {
                return HttpNotFound();
            }
            return View(order);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Process(int id, string actionType) // actionType là "Approve" hoặc "Cancel"
        {
            Order order = db.Orders.Find(id);

            if (order == null)
            {
                return HttpNotFound();
            }

            if (actionType == "Approve")
            {
                order.OrderStatus = "Đã duyệt";
                TempData["SuccessMessage"] = $"Đơn hàng #{id} đã được DUYỆT thành công!";
            }
            else if (actionType == "Cancel")
            {
                order.OrderStatus = "Đã hủy";
                // Cần thêm logic gửi email thông báo cho khách hàng
                TempData["SuccessMessage"] = $"Đơn hàng #{id} đã bị HỦY.";
            }

            db.Entry(order).State = EntityState.Modified;
            db.SaveChanges();
            return RedirectToAction("Index");
        }
    }
}
