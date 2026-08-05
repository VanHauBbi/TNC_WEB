using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Validation;
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

        private void PopulateCouponForm(int[] selectedProducts = null)
        {
            ViewBag.Categories = new SelectList(db.Categories.OrderBy(c => c.CategoryName), "CategoryID", "CategoryName");
            ViewBag.ProductID = new MultiSelectList(db.Products.OrderBy(p => p.ProductName),
                "ProductID", "ProductName", selectedProducts);
        }

        private static void ApplyManualCouponDefaults(Coupon coupon)
        {
            bool usesPercentage = coupon.DiscountPercentage.HasValue && coupon.DiscountPercentage.Value > 0m;
            coupon.CouponType = "GLOBAL";
            coupon.DiscountType = usesPercentage ? "PERCENT" : "FIXED";
            coupon.FixedDiscountAmount = usesPercentage ? null : coupon.MaxDiscountAmount;
            coupon.MinimumOrderValue = 0m;
            coupon.StartDate = DateTime.Now;
            coupon.IsActive = true;
            coupon.IsStackable = false;
            coupon.CampaignID = null;
            coupon.SourceType = "MANUAL";
        }

        private CustomerCoupon FindPersonalVoucher(int couponId)
        {
            return db.CustomerCoupons
                .Where(v => v.CouponID == couponId)
                .OrderByDescending(v => v.AssignedAt)
                .FirstOrDefault();
        }

        private ActionResult RedirectToPersonalVoucher(CustomerCoupon voucher, string actionName)
        {
            return RedirectToAction(actionName, "Marketing", new
            {
                area = "Admin",
                id = voucher.CustomerCouponID
            });
        }

        // GET: Admin/Coupons
        public ActionResult Index()
        {
            var coupons = db.Coupons
                .Include(c => c.CustomerCoupons)
                .OrderByDescending(c => c.CouponID)
                .ToList();
            return View(coupons);
        }

        // GET: Admin/Coupons/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            var personalVoucher = FindPersonalVoucher(id.Value);
            if (personalVoucher != null)
                return RedirectToPersonalVoucher(personalVoucher, "PersonalVoucherDetails");

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

            PopulateCouponForm();

            return View(newCoupon);
        }

        // POST: Admin/Coupons/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        // BỔ SUNG: Thêm tham số mảng int[] selectedProducts để hứng dữ liệu từ giao diện
        public ActionResult Create([Bind(Include = "CouponID,CouponName,Code,DiscountPercentage,MaxDiscountAmount,ExpiryDate,UsageLimit")] Coupon coupon, int[] selectedProducts)
        {
            ApplyManualCouponDefaults(coupon);

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
                    PopulateCouponForm(selectedProducts);
                    return View(coupon);
                }

                if (coupon.ExpiryDate <= DateTime.Now)
                {
                    ModelState.AddModelError("ExpiryDate", "Ngày hết hạn phải lớn hơn ngày, giờ hiện tại.");
                    PopulateCouponForm(selectedProducts);
                    return View(coupon);
                }

                if (coupon.UsageLimit < 1)
                {
                    ModelState.AddModelError("UsageLimit", "Giới hạn sử dụng phải lớn hơn hoặc bằng 1.");
                    PopulateCouponForm(selectedProducts);
                    return View(coupon);
                }

                if ((!coupon.DiscountPercentage.HasValue || coupon.DiscountPercentage.Value <= 0m)
                    && (!coupon.MaxDiscountAmount.HasValue || coupon.MaxDiscountAmount.Value <= 0m))
                {
                    ModelState.AddModelError("", "Vui lòng nhập phần trăm giảm hoặc số tiền giảm.");
                    PopulateCouponForm(selectedProducts);
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

                try
                {
                    db.Coupons.Add(coupon);
                    db.SaveChanges();
                }
                catch (DbEntityValidationException ex)
                {
                    foreach (var entityErrors in ex.EntityValidationErrors)
                    {
                        foreach (var validationError in entityErrors.ValidationErrors)
                            ModelState.AddModelError(validationError.PropertyName ?? "", validationError.ErrorMessage);
                    }
                    PopulateCouponForm(selectedProducts);
                    return View(coupon);
                }

                TempData["SuccessMessage"] = "Thêm mã giảm giá mới thành công!";
                return RedirectToAction("Index");
            }

            // Nạp lại danh sách nếu Form không hợp lệ
            PopulateCouponForm(selectedProducts);
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

            var personalVoucher = FindPersonalVoucher(id.Value);
            if (personalVoucher != null)
            {
                if (personalVoucher.Status == "USED")
                {
                    TempData["ErrorMessage"] = "Voucher đã sử dụng chỉ được lưu lịch sử, không thể chỉnh sửa.";
                    return RedirectToAction("Index");
                }
                return RedirectToPersonalVoucher(personalVoucher, "EditPersonalVoucher");
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
            var personalVoucher = FindPersonalVoucher(coupon.CouponID);
            if (personalVoucher != null)
            {
                TempData["ErrorMessage"] = personalVoucher.Status == "USED"
                    ? "Voucher đã sử dụng chỉ được lưu lịch sử, không thể chỉnh sửa."
                    : "Voucher cá nhân phải được chỉnh sửa tại trang Marketing Selling.";
                return RedirectToAction("Index");
            }

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
                    bool usesPercentage = coupon.DiscountPercentage.HasValue && coupon.DiscountPercentage.Value > 0m;
                    couponToUpdate.CouponName = coupon.CouponName;
                    couponToUpdate.Code = coupon.Code;
                    couponToUpdate.DiscountPercentage = coupon.DiscountPercentage;
                    couponToUpdate.MaxDiscountAmount = coupon.MaxDiscountAmount;
                    couponToUpdate.DiscountType = usesPercentage ? "PERCENT" : "FIXED";
                    couponToUpdate.FixedDiscountAmount = usesPercentage ? null : coupon.MaxDiscountAmount;
                    couponToUpdate.ExpiryDate = coupon.ExpiryDate;
                    couponToUpdate.UsageLimit = coupon.UsageLimit;
                    couponToUpdate.CouponType = string.IsNullOrWhiteSpace(couponToUpdate.CouponType) ? "GLOBAL" : couponToUpdate.CouponType;
                    couponToUpdate.SourceType = string.IsNullOrWhiteSpace(couponToUpdate.SourceType) ? "MANUAL" : couponToUpdate.SourceType;
                    couponToUpdate.StartDate = couponToUpdate.StartDate ?? DateTime.Now;
                    couponToUpdate.IsActive = true;

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
            var personalVoucher = FindPersonalVoucher(id.Value);
            if (personalVoucher != null)
            {
                if (personalVoucher.Status == "USED")
                {
                    TempData["ErrorMessage"] = "Voucher đã sử dụng được giữ làm lịch sử và không thể xóa.";
                    return RedirectToAction("Index");
                }
                return RedirectToPersonalVoucher(personalVoucher, "DeletePersonalVoucher");
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
            var personalVoucher = FindPersonalVoucher(id);
            if (personalVoucher != null)
            {
                TempData["ErrorMessage"] = personalVoucher.Status == "USED"
                    ? "Voucher đã sử dụng được giữ làm lịch sử và không thể xóa."
                    : "Hãy ngừng voucher cá nhân tại trang Marketing Selling.";
                return RedirectToAction("Index");
            }

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
