using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using PagedList;
using WebBanHang.Models.ViewModel;
using WebBanHang.Models;
using System.Data.Entity;

namespace WebBanHang.Controllers
{
    public class HomeController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();
        public ActionResult Index(string searchTerm, int? page)
        {
            var model = new HomeProductVM();
            var products = db.Products.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                model.SearchTerm = searchTerm;
                products = products.Where(p => p.ProductName.Contains(searchTerm) ||
                                               p.ProductDescription.Contains(searchTerm) ||
                                               p.Category.CategoryName.Contains(searchTerm));
            }

            // Lấy toàn bộ để hiển thị theo từng khối
            model.FeaturedProducts = products.OrderByDescending(p => p.OrderDetails.Count()).ToList();
            model.NewProducts = products.OrderByDescending(p => p.ProductID).ToList();

            return View(model);
        }

        public ActionResult Product(int? page, string searchTerm)
        {
            int pageSize = 6;
            int pageNumber = (page ?? 1);

            var products = db.Products.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                products = products.Where(p => p.ProductName.Contains(searchTerm));
            }

            var model = products.OrderBy(p => p.ProductName)
                                .ToPagedList(pageNumber, pageSize);

            var homeVM = new HomeProductVM();
            homeVM.PageNumber = pageNumber;
            homeVM.Products = model;

            return View(homeVM);
        }

        // ==========================================================
        public ActionResult ProductDetail(int? id)
        {
            if (id == null)
            {
                // Nếu không có id, trả về lỗi 400 Bad Request
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // Dùng Include() để tải thông tin Category và OrderDetails
            // View "ProductDetail.cshtml" của bạn cần 2 thông tin này:
            // 1. @Model.product.Category.CategoryName
            // 2. @Model.product.OrderDetails.Count
            Product product = db.Products
                                .Include(p => p.Category)
                                .Include(p => p.OrderDetails)
                                .SingleOrDefault(p => p.ProductID == id);

            if (product == null)
            {
                // Nếu không tìm thấy sản phẩm, trả về lỗi 404
                return HttpNotFound();
            }

            // Tạo ViewModel (ProductDetailsVM) mà View của bạn đang dùng
            var viewModel = new ProductDetailsVM
            {
                product = product,
                quantity = 1, // Số lượng mặc định là 1

                // Lấy các sản phẩm liên quan (ví dụ: 4 sản phẩm cùng danh mục)
                // View của bạn gọi @Html.Partial("PVTopProduct", Model)
                // Tôi sẽ điền vào 'RelatedProducts' để làm mẫu
                RelatedProducts = db.Products
                                    .Where(p => p.CategoryID == product.CategoryID && p.ProductID != id)
                                    .OrderByDescending(p => p.ProductID) // Sắp xếp tuỳ ý
                                    .ToPagedList(1, 4) // Lấy 4 sản phẩm ở trang 1
            };

            // Trả về View "ProductDetail.cshtml" với ViewModel đã tạo
            return View(viewModel);
        }

        public ActionResult Checkout()
        {
            return View();
        }

        public ActionResult GioiThieu()
        {
            return View();
        }

        public ActionResult GioHang()
        {
            // Thay vì render view trực tiếp, ta điều hướng sang CartController
            return RedirectToAction("Index", "Cart");
        }

        public ActionResult _HeaderCategory()
        {
            var categories = db.Categories.ToList();
            return PartialView("_HeaderCategory", categories);
        }

        // ==========================================================
        // TÌM KIẾM SẢN PHẨM (ACTION ĐÃ CẬP NHẬT)
        // ==========================================================
        public ActionResult ProductSearch(string query)
        {
            // Test Case ProductDT07: Để trống
            // Xử lý server-side (dù client đã có 'required')
            if (string.IsNullOrWhiteSpace(query))
            {
                TempData["ErrorMessage"] = "Vui lòng nhập từ khóa";
                return RedirectToAction("Index"); // Redirect về trang chủ và hiển thị lỗi
            }

            // Test Case ProductDT08: Vượt quá độ dài
            const int MAX_LENGTH = 100;
            if (query.Length > MAX_LENGTH)
            {
                TempData["ErrorMessage"] = "Từ khóa tìm kiếm quá dài (tối đa 100 ký tự).";
                return RedirectToAction("Index");
            }

            // --- CẢI TIẾN LOGIC TÌM KIẾM ---
            string searchQuery = query.ToLower().Trim();

            // ProductDT03, DT04: Tìm kiếm (case-insensitive) trên nhiều trường
            // Giống hệt logic ở action Index của bạn để nhất quán
            var searchResults = db.Products
                .Include(p => p.Category) // Cần Include để truy vấn Category.CategoryName
                .Where(p =>
                    p.ProductName.ToLower().Contains(searchQuery) ||
                    p.ProductDescription.ToLower().Contains(searchQuery) ||
                    p.Category.CategoryName.ToLower().Contains(searchQuery)
                )
                .ToList();

            ViewBag.SearchQuery = query; // Giữ lại từ khóa gốc để hiển thị

            // ProductDT06 (Không tồn tại) sẽ được xử lý bởi View "SearchResults.cshtml"
            // (File SearchResults.cshtml của bạn đã xử lý tốt phần này)
            return View("SearchResults", searchResults);
        }

        // (Nhớ giữ lại: using PagedList; và using System.Data.Entity;)

        public ActionResult DanhMucSanPham(
            int? id,           // ID Danh mục
            string priceRange, // Lọc giá
            string sortBy,     // THÊM MỚI: Tham số Sắp xếp
            int? page
        )
        {
            if (id == null)
            {
                return RedirectToAction("Index");
            }

            var category = db.Categories.Find(id.Value);
            if (category == null)
            {
                return HttpNotFound();
            }

            // 1. Lấy sản phẩm
            var products = db.Products.Where(p => p.CategoryID == id.Value).AsQueryable();

            // 2. Lọc theo Khoảng giá (logic cũ giữ nguyên)
            switch (priceRange)
            {
                case "duoi-5":
                    products = products.Where(p => p.ProductPrice < 5000000);
                    break;
                case "5-10":
                    products = products.Where(p => p.ProductPrice >= 5000000 && p.ProductPrice <= 10000000);
                    break;
                case "tren-10":
                    products = products.Where(p => p.ProductPrice > 10000000);
                    break;
            }

            // 3. SẮP XẾP (Đã cập nhật logic)
            switch (sortBy)
            {
                case "price-asc": // Giá Thấp - Cao
                    products = products.OrderBy(p => p.ProductPrice);
                    break;
                case "price-desc": // Giá Cao - Thấp
                    products = products.OrderByDescending(p => p.ProductPrice);
                    break;
                case "name-asc": // Tên: A-Z
                    products = products.OrderBy(p => p.ProductName);
                    break;
                case "name-desc": // Tên: Z-A
                    products = products.OrderByDescending(p => p.ProductName);
                    break;
                default: // Mặc định: Mới nhất
                    products = products.OrderByDescending(p => p.ProductID);
                    break;
            }

            // 4. Lưu lựa chọn lọc vào ViewBag
            ViewBag.CategoryName = category.CategoryName;
            ViewBag.CategoryId = id.Value;
            ViewBag.CurrentPriceRange = priceRange;
            ViewBag.CurrentSortBy = sortBy; // THÊM MỚI: Truyền sortBy ra View

            // 5. Phân trang
            int pageSize = 9;
            int pageNumber = (page ?? 1);
            var pagedProducts = products.ToPagedList(pageNumber, pageSize);

            return View(pagedProducts);
        }

    }
}
