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
                    var order = db.Orders
                                  .Include(o => o.OrderDetails)
                                  .SingleOrDefault(o => o.OrderID == id);

                    if (order == null)
                    {
                        TempData["ErrorMessage"] = "Không tìm thấy đơn hàng cần xóa.";
                        return RedirectToAction("Index");
                    }

                    db.OrderDetails.RemoveRange(order.OrderDetails);
                    db.Orders.Remove(order);

                    db.SaveChanges();
                    transaction.Commit();

                    TempData["SuccessMessage"] = $"✅ Đã xóa hoàn toàn đơn hàng #{id} khỏi hệ thống.";
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["ErrorMessage"] = "❌ Lỗi xóa: Không thể xóa đơn hàng. Lỗi chi tiết: " + ex.Message;
                }
            }

            return RedirectToAction("Index");
        }

        // GET: Admin/Orders/Process/5
        public ActionResult Process(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            var order = db.Orders.Include(o => o.Customer).SingleOrDefault(o => o.OrderID == id);
            if (order == null)
            {
                return HttpNotFound();
            }
            return View(order);
        }

        // POST: Admin/Orders/Process/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Process(int id, string actionType)
        {
            Order order = db.Orders.Include(o => o.OrderDetails).SingleOrDefault(o => o.OrderID == id);
            if (order == null) return HttpNotFound();

            // =========================================================================
            // CHỐT CHẶN BẢO MẬT TUYỆT ĐỐI: KHÔNG CHO PHÉP CHẠY CODE NẾU ĐƠN ĐÃ HỎNG
            // =========================================================================
            if (order.PaymentStatus == "Thất bại" || order.PaymentStatus == "Đã hủy" || order.OrderStatus == "Đã hủy")
            {
                TempData["ErrorMessage"] = "Lỗi bảo mật: Đơn hàng này đã bị hủy hoặc thanh toán thất bại, không thể thao tác thêm!";
                return RedirectToAction("Process", new { id = order.OrderID });
            }

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    if (actionType == "Approve")
                    {
                        order.OrderStatus = "Đã duyệt";
                        TempData["SuccessMessage"] = $"Đơn hàng #{id} đã được DUYỆT. Hãy chuẩn bị hàng!";
                    }
                    else if (actionType == "Ship")
                    {
                        order.OrderStatus = "Đang giao";
                        TempData["SuccessMessage"] = $"Đơn hàng #{id} đã được chuyển cho đơn vị vận chuyển.";
                    }
                    else if (actionType == "Complete")
                    {
                        order.OrderStatus = "Đã giao";
                        if (order.PaymentMethod == "Tiền mặt")
                        {
                            order.PaymentStatus = "Đã thanh toán";
                        }
                        TempData["SuccessMessage"] = $"Đơn hàng #{id} đã GIAO THÀNH CÔNG và ghi nhận doanh thu.";
                    }
                    else if (actionType == "MarkPaid")
                    {
                        order.PaymentStatus = "Đã thanh toán";
                        TempData["SuccessMessage"] = $"Đã cập nhật trạng thái THANH TOÁN cho đơn hàng #{id}.";
                    }
                    else if (actionType == "Cancel")
                    {
                        if (order.OrderStatus != "Đã hủy")
                        {
                            order.OrderStatus = "Đã hủy";

                            // =========================================================================
                            // HOÀN TRẢ TỒN KHO & KHÔI PHỤC KẾ TOÁN FIFO CHO ĐƠN BỊ ADMIN HỦY
                            // =========================================================================
                            foreach (var detail in order.OrderDetails)
                            {
                                var product = db.Products.Find(detail.ProductID);
                                if (product != null)
                                {
                                    // 1. Trả tồn kho bề mặt
                                    product.StockQuantity += detail.Quantity;

                                    // 2. Trả tồn kho chiều sâu (Lô hàng FIFO)
                                    var latestBatch = db.ImportReceiptDetails
                                                        .Where(b => b.ProductID == detail.ProductID)
                                                        .OrderByDescending(b => b.DetailID)
                                                        .FirstOrDefault();
                                    if (latestBatch != null)
                                    {
                                        latestBatch.RemainingQuantity += detail.Quantity;
                                        db.Entry(latestBatch).State = EntityState.Modified;
                                    }
                                }
                            }
                            TempData["SuccessMessage"] = $"Đơn hàng #{id} đã bị HỦY. Đã tự động hoàn trả tồn kho đầy đủ.";
                        }
                    }

                    db.Entry(order).State = EntityState.Modified;
                    db.SaveChanges();
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["ErrorMessage"] = "Lỗi xử lý: " + ex.Message;
                }
            }

            return RedirectToAction("Process", new { id = order.OrderID });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateOrderStatus(int orderId, string newStatus)
        {
            var order = db.Orders.Find(orderId);
            if (order != null)
            {
                order.OrderStatus = newStatus;
                db.SaveChanges();
                TempData["Message"] = "Cập nhật trạng thái đơn hàng thành công!";
            }
            else
            {
                TempData["Error"] = "Không tìm thấy đơn hàng!";
            }

            // Quay lại trang chi tiết đơn hàng của Admin
            return RedirectToAction("Details", new { id = orderId });
        }
    }
}