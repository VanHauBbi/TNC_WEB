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

        // ==========================================================
        // 1. TRANG CHỦ (INDEX)
        // ==========================================================
        public ActionResult Index(string searchTerm, int? page)
        {
            var model = new HomeProductVM();

            var baseQuery = db.Products.Include(p => p.Category).Include(p => p.OrderDetails).Include(p => p.Coupons).Where(p => p.Status != 2).AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                model.SearchTerm = searchTerm;
                baseQuery = baseQuery.Where(p => p.ProductName.Contains(searchTerm) || p.ProductDescription.Contains(searchTerm) || p.Category.CategoryName.Contains(searchTerm));
            }

            model.FeaturedProducts = baseQuery.OrderByDescending(p => p.OrderDetails.Count()).Take(8).ToList();
            model.NewProducts = baseQuery.OrderByDescending(p => p.ProductID).Take(8).ToList();

            // =====================================================================
            // BỘ NÃO AI TRÊN TRANG CHỦ (KIẾN TRÚC MẢNG LỚN)
            // =====================================================================
            var mySessionId = Session.SessionID;
            int? currentCustomerId = Session["CustomerID"] as int?;

            // MẢNG LỚN CHỨA SẴN 12 Ô SẢN PHẨM & QUẢN LÝ ID CHỐNG TRÙNG
            var finalRecommendations = new List<Product>();
            var excludeIds = new List<int>();

            // Lấy dữ liệu cảm biến (Đã fix lỗi Session cho khách vãng lai)
            var recentKeywords = db.UserBehaviorLogs
                .Where(l => l.SessionID == mySessionId && l.ActionType == "SEARCH_KEYWORD" && !string.IsNullOrEmpty(l.SearchKeyword))
                .OrderByDescending(l => l.CreatedAt).Select(l => l.SearchKeyword).ToList().Distinct().Take(5).ToList();

            if (!recentKeywords.Any() && currentCustomerId != null)
            {
                recentKeywords = db.UserBehaviorLogs
                    .Where(l => l.CustomerID == currentCustomerId && l.ActionType == "SEARCH_KEYWORD" && !string.IsNullOrEmpty(l.SearchKeyword))
                    .OrderByDescending(l => l.CreatedAt).Select(l => l.SearchKeyword).ToList().Distinct().Take(5).ToList();
            }

            var recentInteractedIds = db.UserBehaviorLogs
                .Where(l => (currentCustomerId != null ? l.CustomerID == currentCustomerId : l.SessionID == mySessionId) && l.ProductID != null)
                .OrderByDescending(l => l.CreatedAt).Select(l => l.ProductID.Value).Distinct().Take(3).ToList();

            bool isGuest = currentCustomerId == null;
            bool hasLog = recentKeywords.Any() || recentInteractedIds.Any();

            IQueryable<SmartRecommendation> queryRecommendations = null;
            if (recentInteractedIds.Any()) queryRecommendations = db.SmartRecommendations.Where(r => recentInteractedIds.Contains(r.ProductID_A));

            // =====================================================
            // BOX 1: TWO-PHASE (LỢI NHUẬN) - Xử lý trường hợp nội bộ
            // =====================================================
            int targetTwoPhase = 4; // Mặc định: Có Log hoặc User chưa log
            if (isGuest && !hasLog) targetTwoPhase = 6; // Vãng lai chưa log

            var twoPhaseProducts = new List<Product>();
            if (queryRecommendations != null)
            {
                var rawTwoPhase = queryRecommendations.OrderByDescending(r => r.ActualUtility).Take(30).Select(r => r.Product1).Distinct().ToList();
                foreach (var item in rawTwoPhase)
                {
                    if (twoPhaseProducts.Count >= targetTwoPhase) break;
                    if (excludeIds.Contains(item.ProductID)) continue;

                    string itemType = item.ComponentType ?? "";
                    if (twoPhaseProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 2) continue;

                    twoPhaseProducts.Add(item);
                    excludeIds.Add(item.ProductID);
                }
            }
            if (twoPhaseProducts.Count < targetTwoPhase) // Vét cạn Box 1
            {
                var fill = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2)
                                      .OrderByDescending(p => p.ProductPrice).Take(targetTwoPhase - twoPhaseProducts.Count).ToList();
                twoPhaseProducts.AddRange(fill);
                excludeIds.AddRange(fill.Select(p => p.ProductID));
            }

            // Kéo vào Mảng Lớn
            twoPhaseProducts = twoPhaseProducts.OrderBy(x => Guid.NewGuid()).ToList();
            finalRecommendations.AddRange(twoPhaseProducts);


            // =====================================================
            // BOX 2: APRIORI (CỘNG ĐỒNG) - Xử lý trường hợp nội bộ
            // =====================================================
            int targetApriori = 4; // Mặc định: Khách có log
            if (isGuest && !hasLog) targetApriori = 6; // Vãng lai chưa log
            else if (!isGuest && !hasLog) targetApriori = 8; // User chuẩn chưa log

            var aprioriProducts = new List<Product>();
            if (queryRecommendations != null)
            {
                var rawApriori = queryRecommendations.OrderByDescending(r => r.Confidence).ThenByDescending(r => r.Support).Take(30).Select(r => r.Product1).Distinct().ToList();
                foreach (var item in rawApriori)
                {
                    if (aprioriProducts.Count >= targetApriori) break;
                    if (excludeIds.Contains(item.ProductID)) continue;

                    string itemType = item.ComponentType ?? "";
                    if (aprioriProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 2) continue;

                    aprioriProducts.Add(item);
                    excludeIds.Add(item.ProductID);
                }
            }
            if (aprioriProducts.Count < targetApriori) // Vét cạn Box 2
            {
                var fill = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2)
                                      .OrderByDescending(p => p.OrderDetails.Count).Take(targetApriori - aprioriProducts.Count).ToList();
                aprioriProducts.AddRange(fill);
                excludeIds.AddRange(fill.Select(p => p.ProductID));
            }

            if (targetApriori > 4) aprioriProducts = aprioriProducts.OrderBy(x => Guid.NewGuid()).ToList();
            finalRecommendations.AddRange(aprioriProducts);


            // =====================================================
            // BOX 3: BEHAVIOR (TỪ KHÓA) - Xử lý trường hợp nội bộ
            // =====================================================
            int targetBehavior = hasLog ? 4 : 0;
            var behaviorProducts = new List<Product>();

            if (targetBehavior > 0 && recentKeywords.Any())
            {
                // DUYỆT ƯU TIÊN: Lấy từ khóa mới nhất (kw[0]) đi tìm trước, thiếu mới dùng từ khóa cũ
                foreach (var kw in recentKeywords)
                {
                    if (behaviorProducts.Count >= targetBehavior) break;

                    var rawKeywordRecs = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2)
                        .Where(p => p.ProductName.ToLower().Contains(kw.ToLower()) || p.Category.CategoryName.ToLower().Contains(kw.ToLower()))
                        .OrderByDescending(p => db.UserBehaviorLogs.Where(l => l.ProductID == p.ProductID).Sum(l => (int?)l.ActionWeight) ?? 0)
                        .Take(10).ToList();

                    foreach (var item in rawKeywordRecs)
                    {
                        if (behaviorProducts.Count >= targetBehavior) break;
                        if (excludeIds.Contains(item.ProductID)) continue;

                        string itemType = item.ComponentType ?? "";
                        // Dùng số 2 cho hàm Index, số 3 cho hàm ProductDetail
                        if (behaviorProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 2) continue;

                        behaviorProducts.Add(item);
                        excludeIds.Add(item.ProductID);
                    }
                }

                // Vét cạn Box 3
                if (behaviorProducts.Count < targetBehavior)
                {
                    var fill = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2)
                                          .OrderByDescending(p => p.ProductID).Take(targetBehavior - behaviorProducts.Count).ToList();
                    behaviorProducts.AddRange(fill);
                    excludeIds.AddRange(fill.Select(p => p.ProductID));
                }
                finalRecommendations.AddRange(behaviorProducts);
            }

            // Đẩy ra View (Tôi vẫn gán vào 3 biến cũ để mã HTML View của ông ko bị lỗi)
            ViewBag.SmartRecommendations = finalRecommendations;

            return View(model);
        }

        // ==========================================================
        // 2. CHI TIẾT SẢN PHẨM & AI GỢI Ý MUA KÈM
        // ==========================================================
        public ActionResult ProductDetail(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            Product product = db.Products.Include(p => p.Category).Include(p => p.OrderDetails).Include(p => p.Coupons).SingleOrDefault(p => p.ProductID == id);
            if (product == null) return HttpNotFound();

            // MẢNG LỚN CHỨA SẴN 12 Ô SẢN PHẨM & QUẢN LÝ ID CHỐNG TRÙNG
            var finalRecommendations = new List<Product>();
            var excludeIds = new List<int> { product.ProductID };

            var mySessionId = Session.SessionID;
            int? currentCustomerId = Session["CustomerID"] as int?;

            var recentKeywords = db.UserBehaviorLogs
                .Where(l => l.SessionID == mySessionId && l.ActionType == "SEARCH_KEYWORD" && !string.IsNullOrEmpty(l.SearchKeyword))
                .OrderByDescending(l => l.CreatedAt).Select(l => l.SearchKeyword).ToList().Distinct().Take(5).ToList();

            if (!recentKeywords.Any() && currentCustomerId != null)
            {
                recentKeywords = db.UserBehaviorLogs
                    .Where(l => l.CustomerID == currentCustomerId && l.ActionType == "SEARCH_KEYWORD" && !string.IsNullOrEmpty(l.SearchKeyword))
                    .OrderByDescending(l => l.CreatedAt).Select(l => l.SearchKeyword).ToList().Distinct().Take(5).ToList();
            }

            bool isGuest = currentCustomerId == null;
            bool hasKeywordLog = recentKeywords.Any();
            var queryRecommendations = db.SmartRecommendations.Where(r => r.ProductID_A == id);

            // =====================================================
            // BOX 1: TWO-PHASE (LỢI NHUẬN)
            // =====================================================
            int targetTwoPhase = 4;
            if (isGuest && !hasKeywordLog) targetTwoPhase = 6;

            var twoPhaseProducts = new List<Product>();
            var rawTwoPhase = queryRecommendations.OrderByDescending(r => r.ActualUtility).Take(30).Select(r => r.Product1).ToList();

            foreach (var item in rawTwoPhase)
            {
                if (twoPhaseProducts.Count >= targetTwoPhase) break;
                if (excludeIds.Contains(item.ProductID)) continue;

                string itemType = item.ComponentType ?? "";
                if (twoPhaseProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 3) continue;

                twoPhaseProducts.Add(item);
                excludeIds.Add(item.ProductID);
            }
            if (twoPhaseProducts.Count < targetTwoPhase)
            {
                var fill = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2)
                                      .OrderByDescending(p => p.ProductPrice).Take(targetTwoPhase - twoPhaseProducts.Count).ToList();
                twoPhaseProducts.AddRange(fill);
                excludeIds.AddRange(fill.Select(p => p.ProductID));
            }

            twoPhaseProducts = twoPhaseProducts.OrderBy(x => Guid.NewGuid()).ToList();
            finalRecommendations.AddRange(twoPhaseProducts);


            // =====================================================
            // BOX 2: APRIORI (CỘNG ĐỒNG)
            // =====================================================
            int targetApriori = 4;
            if (isGuest && !hasKeywordLog) targetApriori = 6;
            else if (!isGuest && !hasKeywordLog) targetApriori = 8;

            var aprioriProducts = new List<Product>();
            var rawApriori = queryRecommendations.OrderByDescending(r => r.Confidence).ThenByDescending(r => r.Support).Take(30).Select(r => r.Product1).ToList();

            foreach (var item in rawApriori)
            {
                if (aprioriProducts.Count >= targetApriori) break;
                if (excludeIds.Contains(item.ProductID)) continue;

                string itemType = item.ComponentType ?? "";
                if (aprioriProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 3) continue;

                aprioriProducts.Add(item);
                excludeIds.Add(item.ProductID);
            }

            // Vét cạn Lớp 1 (Cùng Danh mục)
            if (aprioriProducts.Count < targetApriori)
            {
                var fillSameCat = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2 && p.CategoryID == product.CategoryID)
                                             .OrderByDescending(p => p.OrderDetails.Count).Take(targetApriori - aprioriProducts.Count).ToList();
                aprioriProducts.AddRange(fillSameCat);
                excludeIds.AddRange(fillSameCat.Select(p => p.ProductID));
            }
            // Vét cạn Lớp 2 (Toàn Shop)
            if (aprioriProducts.Count < targetApriori)
            {
                var fillAllShop = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2)
                                             .OrderByDescending(p => p.OrderDetails.Count).Take(targetApriori - aprioriProducts.Count).ToList();
                aprioriProducts.AddRange(fillAllShop);
                excludeIds.AddRange(fillAllShop.Select(p => p.ProductID));
            }

            if (targetApriori > 4) aprioriProducts = aprioriProducts.OrderBy(x => Guid.NewGuid()).ToList();
            finalRecommendations.AddRange(aprioriProducts);


            // =====================================================
            // BOX 3: BEHAVIOR (TỪ KHÓA)
            // =====================================================
            int targetBehavior = hasKeywordLog ? 4 : 0;
            var behaviorProducts = new List<Product>();

            if (targetBehavior > 0)
            {
                string kw1 = recentKeywords.Count > 0 ? recentKeywords[0] : null;
                string kw2 = recentKeywords.Count > 1 ? recentKeywords[1] : null;

                var rawKeywordRecs = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2)
                    .Where(p => (kw1 != null && (p.ProductName.ToLower().Contains(kw1.ToLower()) || p.Category.CategoryName.ToLower().Contains(kw1.ToLower()))) ||
                                (kw2 != null && (p.ProductName.ToLower().Contains(kw2.ToLower()) || p.Category.CategoryName.ToLower().Contains(kw2.ToLower()))))
                    .OrderByDescending(p => db.UserBehaviorLogs.Where(l => l.ProductID == p.ProductID).Sum(l => (int?)l.ActionWeight) ?? 0)
                    .Take(20).ToList();

                foreach (var item in rawKeywordRecs)
                {
                    if (behaviorProducts.Count >= targetBehavior) break;
                    if (excludeIds.Contains(item.ProductID)) continue;

                    string itemType = item.ComponentType ?? "";
                    if (behaviorProducts.Count(x => (x.ComponentType ?? "") == itemType) >= 3) continue;

                    behaviorProducts.Add(item);
                    excludeIds.Add(item.ProductID);
                }

                if (behaviorProducts.Count < targetBehavior)
                {
                    var fill = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && p.Status != 2)
                                          .OrderByDescending(p => p.ProductID).Take(targetBehavior - behaviorProducts.Count).ToList();
                    behaviorProducts.AddRange(fill);
                    excludeIds.AddRange(fill.Select(p => p.ProductID));
                }
                finalRecommendations.AddRange(behaviorProducts);
            }

            // Gửi ra View
            ViewBag.SmartRecommendations = finalRecommendations;

            var viewModel = new ProductDetailsVM
            {
                product = product,
                quantity = 1,
                RelatedProducts = db.Products.Where(p => p.CategoryID == product.CategoryID && p.ProductID != id).OrderByDescending(p => p.ProductID).ToPagedList(1, 4)
            };

            return View(viewModel);
        }

        // ==========================================================
        // 3. TÌM KIẾM VÀ GHI LOG SEARCH AI
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
            int? currentCustomerId = Session["CustomerID"] as int?;

            // 🛑 1. CHỈ GHI LOG TỪ KHÓA NẾU KHÁCH ĐÃ ĐĂNG NHẬP
            if (currentCustomerId != null)
            {
                try
                {
                    // Tìm xem từ khóa này khách đã search bao giờ chưa
                    var currentSearchLog = db.UserBehaviorLogs.FirstOrDefault(l =>
                        l.CustomerID == currentCustomerId
                        && l.ActionType == "SEARCH_KEYWORD"
                        && l.SearchKeyword == searchQuery);

                    if (currentCustomerId != null)
                    {
                        try
                        {
                            // KHÔNG CẦN KIỂM TRA CŨ HAY MỚI, CỨ SEARCH LÀ BẮT BUỘC ĐẺ THÊM DÒNG LOG!
                            db.UserBehaviorLogs.Add(new UserBehaviorLog
                            {
                                SessionID = Session.SessionID,
                                CustomerID = currentCustomerId,
                                ProductID = null,
                                ActionType = "SEARCH_KEYWORD",
                                SearchKeyword = searchQuery,
                                ActionWeight = 0,
                                CreatedAt = DateTime.Now
                            });

                            // Lưu thẳng xuống DB
                            db.SaveChanges();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("Lỗi ghi log Search: " + ex.Message);
                        }
                    }
                    else
                    {
                        // CÓ RỒI -> Chỉ cập nhật thời gian lên hiện tại để AI ưu tiên
                        currentSearchLog.CreatedAt = DateTime.Now;
                        db.Entry(currentSearchLog).State = System.Data.Entity.EntityState.Modified;
                    }

                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Lỗi ghi log Search: " + ex.Message);
                }
            }

            // 2. LẤY KẾT QUẢ TÌM KIẾM
            var searchResults = db.Products
                .Include(p => p.Category)
                .Include(p => p.Coupons)
                .Where(p => p.Status != 2 &&
                    (p.ProductName.ToLower().Contains(searchQuery) ||
                     p.ProductDescription.ToLower().Contains(searchQuery) ||
                     p.Category.CategoryName.ToLower().Contains(searchQuery))
                ).ToList();

            ViewBag.SearchQuery = query;

            // 3. LẤP ĐẦY TRANG CHO ĐỦ 12 SẢN PHẨM (Ưu tiên đồ đắt/Lời)
            if (searchResults.Count < 12)
            {
                var existingIds = searchResults.Select(p => p.ProductID).ToList();
                int needMore = 12 - searchResults.Count;

                var highProfitProducts = db.Products
                    .Include(p => p.Category)
                    .Include(p => p.Coupons)
                    .Where(p => !existingIds.Contains(p.ProductID) && p.Status != 2)
                    .OrderByDescending(p => p.ProductPrice)
                    .Take(needMore).ToList();

                searchResults.AddRange(highProfitProducts);
            }

            return View("SearchResults", searchResults);
        }

        // ==========================================================
        // 4. DANH MỤC VÀ CÁC HÀM PHỤ TRỢ KHÁC
        // ==========================================================
        public ActionResult Product(int? page, string searchTerm)
        {
            int pageSize = 6;
            int pageNumber = (page ?? 1);
            var products = db.Products.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                products = products.Where(p => p.ProductName.Contains(searchTerm));
            }

            var model = products.OrderBy(p => p.ProductName).ToPagedList(pageNumber, pageSize);
            var homeVM = new HomeProductVM { PageNumber = pageNumber, Products = model };
            return View(homeVM);
        }

        public ActionResult Checkout() { return View(); }
        public ActionResult GioiThieu() { return View(); }
        public ActionResult GioHang() { return RedirectToAction("Index", "Cart"); }

        public ActionResult _HeaderCategory()
        {
            var categories = db.Categories.ToList();
            return PartialView("_HeaderCategory", categories);
        }

        public ActionResult DanhMucSanPham(int? id, string priceRange, string sortBy, int? page)
        {
            if (id == null) return RedirectToAction("Index");

            var category = db.Categories.Find(id.Value);
            if (category == null) return HttpNotFound();

            try
            {
                var products = db.Products
                    .Include(p => p.Coupons)
                    .Include(p => p.OrderDetails)
                    .Where(p => p.CategoryID == id.Value && p.Status != 2)
                    .AsQueryable();

                switch (priceRange)
                {
                    case "duoi-5": products = products.Where(p => p.ProductPrice < 5000000); break;
                    case "5-10": products = products.Where(p => p.ProductPrice >= 5000000 && p.ProductPrice <= 10000000); break;
                    case "tren-10": products = products.Where(p => p.ProductPrice > 10000000); break;
                }

                switch (sortBy)
                {
                    case "price-asc": products = products.OrderBy(p => p.ProductPrice); break;
                    case "price-desc": products = products.OrderByDescending(p => p.ProductPrice); break;
                    case "name-asc": products = products.OrderBy(p => p.ProductName); break;
                    case "name-desc": products = products.OrderByDescending(p => p.ProductName); break;
                    default: products = products.OrderByDescending(p => p.ProductID); break;
                }

                ViewBag.CategoryName = category.CategoryName;
                ViewBag.CategoryId = id.Value;
                ViewBag.CurrentPriceRange = priceRange;
                ViewBag.CurrentSortBy = sortBy;

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

        [ChildActionOnly]
        public ActionResult GetRecommendations(int productId)
        {
            var excludeIds = new List<int> { productId };
            string currentSession = Session.SessionID;
            int? currentCustomerId = Session["CustomerID"] as int?;

            var recentKeywords = db.UserBehaviorLogs
                .Where(l => (currentCustomerId != null ? l.CustomerID == currentCustomerId : l.SessionID == currentSession)
                            && l.ActionType == "SEARCH_KEYWORD" && !string.IsNullOrEmpty(l.SearchKeyword))
                .OrderByDescending(l => l.CreatedAt).Select(l => l.SearchKeyword).Distinct().Take(5).ToList();

            bool isGuest = currentCustomerId == null;
            bool hasLog = recentKeywords.Any();

            int targetTwoPhase, targetApriori, targetBehavior;
            if (isGuest) { targetTwoPhase = 6; targetApriori = 6; targetBehavior = 0; }
            else if (!hasLog) { targetTwoPhase = 4; targetApriori = 8; targetBehavior = 0; }
            else { targetTwoPhase = 4; targetApriori = 4; targetBehavior = 4; }

            var query = db.SmartRecommendations.Where(r => r.ProductID_A == productId);

            // TWO-PHASE (Lấy trước)
            var rawTwoPhase = query.OrderByDescending(r => r.ActualUtility).Take(30).Select(r => r.Product1).ToList();
            var twoPhaseProducts = new List<Product>();
            foreach (var item in rawTwoPhase)
            {
                if (twoPhaseProducts.Count >= targetTwoPhase) break;
                if (excludeIds.Contains(item.ProductID)) continue;
                if (twoPhaseProducts.Count(x => (x.ComponentType ?? "") == (item.ComponentType ?? "")) >= 3) continue;
                twoPhaseProducts.Add(item);
                excludeIds.Add(item.ProductID);
            }
            if (twoPhaseProducts.Count < targetTwoPhase)
            {
                var fill = db.Products.Where(p => !excludeIds.Contains(p.ProductID)).OrderByDescending(p => p.ProductPrice).Take(targetTwoPhase - twoPhaseProducts.Count).ToList();
                twoPhaseProducts.AddRange(fill);
                excludeIds.AddRange(fill.Select(p => p.ProductID));
            }
            twoPhaseProducts = twoPhaseProducts.OrderBy(x => Guid.NewGuid()).ToList();

            // APRIORI
            var rawApriori = query.OrderByDescending(r => r.Confidence).ThenByDescending(r => r.Support).Take(30).Select(r => r.Product1).ToList();
            var aprioriProducts = new List<Product>();
            foreach (var item in rawApriori)
            {
                if (aprioriProducts.Count >= targetApriori) break;
                if (excludeIds.Contains(item.ProductID)) continue;
                if (aprioriProducts.Count(x => (x.ComponentType ?? "") == (item.ComponentType ?? "")) >= 3) continue;
                aprioriProducts.Add(item);
                excludeIds.Add(item.ProductID);
            }
            if (aprioriProducts.Count < targetApriori)
            {
                var fill = db.Products.Where(p => !excludeIds.Contains(p.ProductID)).OrderByDescending(p => p.OrderDetails.Count).Take(targetApriori - aprioriProducts.Count).ToList();
                aprioriProducts.AddRange(fill);
                excludeIds.AddRange(fill.Select(p => p.ProductID));
            }

            // BEHAVIOR
            var behaviorProducts = new List<Product>();
            if (targetBehavior > 0)
            {
                string kw1 = recentKeywords.Count > 0 ? recentKeywords[0] : null;
                var rawKw = db.Products.Where(p => !excludeIds.Contains(p.ProductID) && (kw1 != null && p.ProductName.Contains(kw1)))
                                       .OrderByDescending(p => p.ProductPrice).Take(10).ToList();
                foreach (var item in rawKw)
                {
                    if (behaviorProducts.Count >= targetBehavior) break;
                    behaviorProducts.Add(item);
                    excludeIds.Add(item.ProductID);
                }
                if (behaviorProducts.Count < targetBehavior)
                {
                    behaviorProducts.AddRange(db.Products.Where(p => !excludeIds.Contains(p.ProductID)).OrderByDescending(p => p.ProductID).Take(targetBehavior - behaviorProducts.Count).ToList());
                }
            }

            var model = new Tuple<List<Product>, List<Product>, List<Product>>(twoPhaseProducts, aprioriProducts, behaviorProducts);
            return PartialView("_Recommendations", model);
        }
    }
}