using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using WebBanHang.Models;

namespace WebBanHang.Areas.Admin.Controllers
{
    public class CouponsController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // GET: Admin/Coupons
        public ActionResult Index()
        {
            return View(db.Coupons.ToList());
        }

        // GET: Admin/Coupons/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            // BỔ SUNG: Include(c => c.Products) để lấy thông tin sản phẩm áp dụng
            Coupon coupon = db.Coupons.Include(c => c.Products).FirstOrDefault(c => c.CouponID == id);

            if (coupon == null)
            {
                return HttpNotFound();
            }
            return View(coupon);
        }

        // GET: Admin/Coupons/Create
        public ActionResult Create()
        {
            Coupon newCoupon = new Coupon();
            newCoupon.UsageLimit = 1;
            newCoupon.ExpiryDate = DateTime.Now.AddDays(7);

            // BỔ SUNG: Truyền danh sách Danh mục (Category) sang View
            ViewBag.Categories = new SelectList(db.Categories, "CategoryID", "CategoryName");

            // Giữ lại ViewBag.ProductID để tránh lỗi tương thích nếu validation thất bại
            ViewBag.ProductID = new MultiSelectList(db.Products, "ProductID", "ProductName");

            return View(newCoupon);
        }

        // POST: Admin/Coupons/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        // BỔ SUNG: Thêm tham số mảng int[] selectedProducts để hứng dữ liệu từ giao diện
        public ActionResult Create([Bind(Include = "CouponID,CouponName,Code,DiscountPercentage,MaxDiscountAmount,ExpiryDate,UsageLimit")] Coupon coupon, int[] selectedProducts)
        {
            if (ModelState.IsValid)
            {
                if (!string.IsNullOrEmpty(coupon.Code))
                {
                    coupon.Code = coupon.Code.Trim().ToUpper();
                }

                bool isCodeExist = db.Coupons.Any(c => c.Code == coupon.Code);
                if (isCodeExist)
                {
                    ModelState.AddModelError("Code", "Mã giảm giá này đã tồn tại trong hệ thống!");
                    ViewBag.ProductID = new MultiSelectList(db.Products, "ProductID", "ProductName", selectedProducts);
                    return View(coupon);
                }

                if (coupon.ExpiryDate <= DateTime.Now)
                {
                    ModelState.AddModelError("ExpiryDate", "Ngày hết hạn phải lớn hơn ngày, giờ hiện tại.");
                    ViewBag.ProductID = new MultiSelectList(db.Products, "ProductID", "ProductName", selectedProducts);
                    return View(coupon);
                }

                if (coupon.UsageLimit < 1)
                {
                    ModelState.AddModelError("UsageLimit", "Giới hạn sử dụng phải lớn hơn hoặc bằng 1.");
                    ViewBag.ProductID = new MultiSelectList(db.Products, "ProductID", "ProductName", selectedProducts);
                    return View(coupon);
                }

                // XỬ LÝ LIÊN KẾT SẢN PHẨM CỤ THỂ
                if (selectedProducts != null && selectedProducts.Length > 0)
                {
                    // Khởi tạo danh sách nếu EF chưa tự khởi tạo
                    if (coupon.Products == null) coupon.Products = new List<Product>();

                    foreach (var pId in selectedProducts)
                    {
                        var product = db.Products.Find(pId);
                        if (product != null)
                        {
                            coupon.Products.Add(product); // Thêm liên kết vào bảng CouponProduct
                        }
                    }
                }

                db.Coupons.Add(coupon);
                db.SaveChanges();

                TempData["SuccessMessage"] = "Thêm mã giảm giá mới thành công!";
                return RedirectToAction("Index");
            }

            // Nạp lại danh sách nếu Form không hợp lệ
            ViewBag.ProductID = new MultiSelectList(db.Products, "ProductID", "ProductName", selectedProducts);
            return View(coupon);
        }

        // BỔ SUNG: API Endpoint phục vụ cho AJAX lấy sản phẩm theo danh mục
        [HttpGet]
        public JsonResult GetProductsByCategory(int categoryId)
        {
            db.Configuration.ProxyCreationEnabled = false; // Ngăn chặn lỗi vòng lặp tham chiếu JSON
            var products = db.Products
                             .Where(p => p.CategoryID == categoryId)
                             .Select(p => new {
                                 ProductID = p.ProductID,
                                 ProductName = p.ProductName
                             })
                             .ToList();

            return Json(products, JsonRequestBehavior.AllowGet);
        }

        // GET: Admin/Coupons/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // BỔ SUNG: Include(c => c.Products) để nạp danh sách sản phẩm đã được liên kết
            Coupon coupon = db.Coupons.Include(c => c.Products).FirstOrDefault(c => c.CouponID == id);

            if (coupon == null)
            {
                return HttpNotFound();
            }

            // Truyền danh sách Danh mục sang View
            ViewBag.Categories = new SelectList(db.Categories, "CategoryID", "CategoryName");

            // Đóng gói danh sách sản phẩm cũ thành JSON để JavaScript có thể đọc được
            var existingProducts = coupon.Products.Select(p => new { id = p.ProductID, name = p.ProductName }).ToList();
            ViewBag.ExistingProductsJson = System.Web.Helpers.Json.Encode(existingProducts);

            return View(coupon);
        }

        // POST: Admin/Coupons/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        // BỔ SUNG: Thêm tham số mảng int[] selectedProducts
        public ActionResult Edit([Bind(Include = "CouponID,CouponName,Code,DiscountPercentage,MaxDiscountAmount,ExpiryDate,UsageLimit")] Coupon coupon, int[] selectedProducts)
        {
            if (ModelState.IsValid)
            {
                if (!string.IsNullOrEmpty(coupon.Code)) coupon.Code = coupon.Code.Trim().ToUpper();

                bool isCodeExist = db.Coupons.Any(c => c.Code == coupon.Code && c.CouponID != coupon.CouponID);
                if (isCodeExist)
                {
                    ModelState.AddModelError("Code", "Mã giảm giá này đã được sử dụng cho một khuyến mãi khác!");
                    ViewBag.Categories = new SelectList(db.Categories, "CategoryID", "CategoryName");
                    return View(coupon);
                }

                if (coupon.ExpiryDate <= DateTime.Now)
                {
                    ModelState.AddModelError("ExpiryDate", "Ngày hết hạn phải lớn hơn ngày, giờ hiện tại.");
                    ViewBag.Categories = new SelectList(db.Categories, "CategoryID", "CategoryName");
                    return View(coupon);
                }

                if (coupon.UsageLimit < 1)
                {
                    ModelState.AddModelError("UsageLimit", "Giới hạn sử dụng phải lớn hơn hoặc bằng 1.");
                    ViewBag.Categories = new SelectList(db.Categories, "CategoryID", "CategoryName");
                    return View(coupon);
                }

                // QUAN TRỌNG: Lấy đối tượng Coupon gốc từ DB kèm theo danh sách Products
                var couponToUpdate = db.Coupons.Include(c => c.Products).FirstOrDefault(c => c.CouponID == coupon.CouponID);

                if (couponToUpdate != null)
                {
                    // Cập nhật các trường thông tin cơ bản
                    couponToUpdate.Code = coupon.Code;
                    couponToUpdate.DiscountPercentage = coupon.DiscountPercentage;
                    couponToUpdate.MaxDiscountAmount = coupon.MaxDiscountAmount;
                    couponToUpdate.ExpiryDate = coupon.ExpiryDate;
                    couponToUpdate.UsageLimit = coupon.UsageLimit;

                    // Cập nhật danh sách sản phẩm liên kết
                    couponToUpdate.Products.Clear(); // Xóa sạch liên kết cũ
                    if (selectedProducts != null && selectedProducts.Length > 0)
                    {
                        foreach (var pId in selectedProducts)
                        {
                            var product = db.Products.Find(pId);
                            if (product != null)
                            {
                                couponToUpdate.Products.Add(product); // Thêm liên kết mới
                            }
                        }
                    }

                    db.Entry(couponToUpdate).State = EntityState.Modified;
                    db.SaveChanges();

                    TempData["SuccessMessage"] = "Cập nhật mã giảm giá thành công!";
                    return RedirectToAction("Index");
                }
            }

            ViewBag.Categories = new SelectList(db.Categories, "CategoryID", "CategoryName");
            return View(coupon);
        }

        // GET: Admin/Coupons/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            // BỔ SUNG: Include(c => c.Products) để hiển thị chi tiết trước khi quyết định xóa
            Coupon coupon = db.Coupons.Include(c => c.Products).FirstOrDefault(c => c.CouponID == id);

            if (coupon == null)
            {
                return HttpNotFound();
            }
            return View(coupon);
        }

        // POST: Admin/Coupons/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Coupon coupon = db.Coupons.Find(id);
            db.Coupons.Remove(coupon);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
