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

            var baseQuery = db.Products
                .Include(p => p.Category)
                .Include(p => p.OrderDetails)
                .Include(p => p.Coupons)
                .Where(p => p.Status != 2)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                model.SearchTerm = searchTerm;
                baseQuery = baseQuery.Where(p => p.ProductName.Contains(searchTerm) ||
                                                 p.ProductDescription.Contains(searchTerm) ||
                                                 p.Category.CategoryName.Contains(searchTerm));
            }

            // 2. Tải Sản phẩm nổi bật (Lấy Top 8 món bán chạy nhất)
            model.FeaturedProducts = baseQuery
                .OrderByDescending(p => p.OrderDetails.Count())
                .Take(8)
                .ToList();

            // 3. Tải Sản phẩm mới nhất (Lấy Top 8 món có ID lớn nhất)
            model.NewProducts = baseQuery
                .OrderByDescending(p => p.ProductID)
                .Take(8)
                .ToList();

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
            Product product = db.Products
                            .Include(p => p.Category)
                            .Include(p => p.OrderDetails)
                            .Include(p => p.Coupons)
                            .SingleOrDefault(p => p.ProductID == id);

            if (product == null)
            {
                // Nếu không tìm thấy sản phẩm, trả về lỗi 404
                return HttpNotFound();
            }

            // --- CẬP NHẬT ĐỂ LẤY SẢN PHẨM GỢI Ý (SMART HYBRID AI) KÈM LỌC ĐA DẠNG DANH MỤC ---
            var queryRecommendations = db.SmartRecommendations.Where(r => r.ProductID_A == id);

            // BƯỚC 1: Lấy danh sách thô (Lấy 15 sản phẩm để có đủ data chạy bộ lọc Đa dạng)
            var rawTwoPhase = queryRecommendations
                .OrderByDescending(r => r.ActualUtility)
                .Take(15)
                .Select(r => r.Product1)
                .ToList();

            var rawApriori = queryRecommendations
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => r.Support)
                .Take(15)
                .Select(r => r.Product1)
                .ToList();

            // BƯỚC 2: Lọc đảm bảo tính ĐA DẠNG (Không cho phép 4 sản phẩm cùng 1 danh mục/nhãn)
            // BOX 1: Lọc cho Two-Phase
            var twoPhaseProducts = new List<Product>();
            foreach (var item in rawTwoPhase)
            {
                if (twoPhaseProducts.Count >= 4) break; // Đủ 4 món thì dừng

                string itemType = item.ComponentType ?? ""; // Gom nhóm PC (rỗng) và các linh kiện khác

                // Nếu trong hộp đã có 3 món CÙNG LOẠI này rồi -> Bỏ qua, nhường 1 slot cuối cho loại khác
                if (twoPhaseProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 3) continue;

                twoPhaseProducts.Add(item);
            }

            // BOX 2: Lọc cho Apriori
            var aprioriProducts = new List<Product>();
            foreach (var item in rawApriori)
            {
                if (aprioriProducts.Count >= 8) break;

                string itemType = item.ComponentType ?? "";
                if (aprioriProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 3) continue;

                aprioriProducts.Add(item);
            }

            // Truyền cả 2 danh sách ra View
            ViewBag.TwoPhaseProducts = twoPhaseProducts;
            ViewBag.AprioriProducts = aprioriProducts;
            // -----------------------------------------------------

            // Tạo ViewModel (ProductDetailsVM) mà View của bạn đang dùng
            var viewModel = new ProductDetailsVM
            {
                product = product,
                quantity = 1, // Số lượng mặc định là 1

                RelatedProducts = db.Products
                                    .Where(p => p.CategoryID == product.CategoryID && p.ProductID != id)
                                    .OrderByDescending(p => p.ProductID)
                                    .ToPagedList(1, 4)
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
        // TÌM KIẾM SẢN PHẨM
        // ==========================================================
        public ActionResult ProductSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                TempData["ErrorMessage"] = "Vui lòng nhập từ khóa";
                return RedirectToAction("Index");
            }

            const int MAX_LENGTH = 100;
            if (query.Length > MAX_LENGTH)
            {
                TempData["ErrorMessage"] = "Từ khóa tìm kiếm quá dài (tối đa 100 ký tự).";
                return RedirectToAction("Index");
            }

            string searchQuery = query.ToLower().Trim();

            var searchResults = db.Products
                .Include(p => p.Category)
                .Include(p => p.Coupons)
                .Where(p =>
                    p.ProductName.ToLower().Contains(searchQuery) ||
                    p.ProductDescription.ToLower().Contains(searchQuery) ||
                    p.Category.CategoryName.ToLower().Contains(searchQuery)
                )
                .ToList();

            ViewBag.SearchQuery = query;
            return View("SearchResults", searchResults);
        }

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

            try
            {
                // 1. Lấy sản phẩm
                var products = db.Products
                    .Include(p => p.Coupons)
                    .Include(p => p.OrderDetails)
                    .Where(p => p.CategoryID == id.Value && p.Status != 2)
                    .AsQueryable();
                // 2. Lọc theo Khoảng giá
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

                // 3. SẮP XẾP
                switch (sortBy)
                {
                    case "price-asc":
                        products = products.OrderBy(p => p.ProductPrice);
                        break;
                    case "price-desc":
                        products = products.OrderByDescending(p => p.ProductPrice);
                        break;
                    case "name-asc":
                        products = products.OrderBy(p => p.ProductName);
                        break;
                    case "name-desc":
                        products = products.OrderByDescending(p => p.ProductName);
                        break;
                    default:
                        products = products.OrderByDescending(p => p.ProductID);
                        break;
                }

                // 4. Lưu lựa chọn lọc vào ViewBag
                ViewBag.CategoryName = category.CategoryName;
                ViewBag.CategoryId = id.Value;
                ViewBag.CurrentPriceRange = priceRange;
                ViewBag.CurrentSortBy = sortBy;

                // 5. Phân trang
                int pageSize = 9;
                int pageNumber = (page ?? 1);
                var pagedProducts = products.ToPagedList(pageNumber, pageSize);

                return View(pagedProducts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi: " + ex.Message);
                TempData["ErrorMessage"] = "Có lỗi xảy ra. Vui lòng thử lại.";
                return RedirectToAction("Index");
            }
        }

        // ==========================================================
        // GỢI Ý MUA HÀNG (SMART HYBRID AI) - DÀNH CHO PARTIAL VIEW
        // ==========================================================
        [ChildActionOnly]
        public ActionResult GetRecommendations(int productId)
        {
            var product = db.Products.Find(productId);
            var query = db.SmartRecommendations.Where(r => r.ProductID_A == productId);

            // BƯỚC 1: Lấy nhiều dữ liệu thô (15 items)
            var rawTwoPhase = query
                .OrderByDescending(r => r.ActualUtility)
                .Take(15).Select(r => r.Product1).ToList();

            var rawApriori = query
                .OrderByDescending(r => r.Confidence).ThenByDescending(r => r.Support)
                .Take(15).Select(r => r.Product1).ToList();

            // BƯỚC 2: Bộ lọc ĐA DẠNG HÓA GIỎ HÀNG (Tối đa 3 sản phẩm trùng danh mục)
            var twoPhaseProducts = new List<Product>();
            foreach (var item in rawTwoPhase)
            {
                if (twoPhaseProducts.Count >= 4) break;
                string itemType = item.ComponentType ?? "";
                if (twoPhaseProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 3) continue;
                twoPhaseProducts.Add(item);
            }

            var aprioriProducts = new List<Product>();
            foreach (var item in rawApriori)
            {
                if (aprioriProducts.Count >= 8) break;
                string itemType = item.ComponentType ?? "";
                if (aprioriProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 3) continue;
                aprioriProducts.Add(item);
            }

            // Gói 2 danh sách vào Tuple để truyền ra Partial View nếu dùng
            var model = new Tuple<List<Product>, List<Product>>(twoPhaseProducts, aprioriProducts);
            return PartialView("_Recommendations", model);
        }
    }
}