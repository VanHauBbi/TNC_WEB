using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using System.Web.Security;
using WebBanHang.Models;

namespace WebBanHang.Areas.Admin.Controllers
{
    // Giả định bạn đã thêm thuộc tính IsActive (bool) vào Customer Model/Database
    public class CustomersController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // GET: Admin/Customers (Hiển thị danh sách)
        public ActionResult Index()
        {
            var customers = db.Customers.ToList();
            return View(customers);
        }

        // GET: Admin/Customers/Details/5 (Hiển thị chi tiết)
        public ActionResult Details(int? id)
        {
            if (id == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            Customer customer = db.Customers.Find(id);

            if (customer == null)
                return HttpNotFound();

            return View(customer);
        }

        // GET: Admin/Customers/Edit/5 (Trang chỉnh sửa/khóa tài khoản)
        public ActionResult Edit(int? id)
        {
            if (id == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            Customer customer = db.Customers.Find(id);

            if (customer == null)
                return HttpNotFound();

            // Truyền cả đối tượng Customer sang View
            return View(customer);
        }

        // POST: Admin/Customers/Edit/5 (Logic Khóa/Mở khóa)
        [HttpPost]
        [ValidateAntiForgeryToken]
        // Bắt buộc phải có thuộc tính IsActive trong Model Customer để Bind được
        public ActionResult Edit([Bind(Include = "CustomerID,CustomerName,CustomerEmail,CustomerPhone,CustomerAddress,Username,IsActive")] Customer customer)
        {
            if (ModelState.IsValid)
            {
                db.Entry(customer).State = EntityState.Modified;
                db.SaveChanges();
                TempData["SuccessMessage"] = $"Đã cập nhật trạng thái khách hàng {customer.CustomerName} thành công!";
                return RedirectToAction("Index");
            }
            return View(customer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }



        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    Customer customer = db.Customers
                                          .Include(c => c.Orders)
                                          .SingleOrDefault(c => c.CustomerID == id);

                    if (customer == null)
                    {
                        TempData["ErrorMessage"] = "Không tìm thấy khách hàng cần xóa.";
                        return RedirectToAction("Index");
                    }

                    // Lấy Tên đăng nhập để xóa tài khoản User liên quan
                    string usernameToDelete = customer.Username;

                    // 1. Xóa các Đơn hàng (và Chi tiết Đơn hàng)
                    foreach (var order in customer.Orders.ToList())
                    {
                        var orderDetails = db.OrderDetails.Where(d => d.OrderID == order.OrderID);
                        db.OrderDetails.RemoveRange(orderDetails);
                        db.Orders.Remove(order);
                    }
                    db.SaveChanges();

                    // 2. Xóa bản ghi trong bảng Customer
                    db.Customers.Remove(customer);

                    // 3. TÌM VÀ XÓA TÀI KHOẢN USER LIÊN QUAN (QUAN TRỌNG)
                    var userAccount = db.Users.SingleOrDefault(u => u.Username == usernameToDelete);
                    if (userAccount != null)
                    {
                        db.Users.Remove(userAccount);
                    }

                    db.SaveChanges(); // Lưu thay đổi cuối cùng (xóa Customer và User)
                    transaction.Commit();

                    // Xóa Session của người dùng nếu họ đang trực tuyến
                    if (Session["CustomerID"] != null && (int)Session["CustomerID"] == id)
                    {
                        FormsAuthentication.SignOut();
                        Session.Clear();
                        Session.Abandon();
                    }

                    TempData["SuccessMessage"] = $"✅ Đã xóa vĩnh viễn khách hàng **{customer.CustomerName}** và tài khoản đăng nhập liên quan.";
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["ErrorMessage"] = "❌ Lỗi xóa: Không thể xóa tài khoản khách hàng. Lỗi: " + ex.Message;
                }
            }
            return RedirectToAction("Index");
        }

        public ActionResult Delete(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            Customer customer = db.Customers.Find(id);
            if (customer == null) return HttpNotFound();
            return View(customer); // Sử dụng View Delete.cshtml (hoặc dùng modal xác nhận)
        }

    }
}