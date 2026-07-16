using System;
using System.Linq;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Services;
using PagedList; // THÊM THƯ VIỆN NÀY ĐỂ PHÂN TRANG

namespace WebBanHang.Areas.Admin.Controllers
{
    public class SmartRecommendationController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // Bổ sung tham số page để phân trang
        public ActionResult Index(int? page)
        {
            var rules = db.SmartRecommendations
                          .Include("Product")
                          .Include("Product1")
                          .OrderByDescending(r => r.ActualUtility)
                          .ThenByDescending(r => r.Confidence);

            // Cấu hình phân trang: 10 sản phẩm 1 trang
            int pageSize = 10;
            int pageNumber = (page ?? 1);

            return View(rules.ToPagedList(pageNumber, pageSize));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RunAlgorithm()
        {
            try
            {
                var hybridService = new SmartRecommendationService();

                // Chạy AI: Yêu cầu Độ tin cậy > 20%, mua chung ít nhất 1 lần, mang lại lợi nhuận > 100k
                hybridService.RunHybridAlgorithm(0.2, 1, 100000m);

                TempData["SuccessMessage"] = "Huấn luyện AI Lai (Hybrid) thành công! Hệ thống đã tìm ra các cặp sản phẩm tối ưu nhất.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi chạy thuật toán: " + ex.Message;
            }

            return RedirectToAction("Index");
        }
        public ActionResult ProductAIDetails(int id)
        {
            // 1. Lấy thông tin cơ bản của Sản phẩm A
            var product = db.Products.Find(id);
            if (product == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy sản phẩm!";
                return RedirectToAction("Index");
            }

            // 2. Lấy toàn bộ dữ liệu AI đã quét liên quan đến Sản phẩm A này
            var rawRecommendations = db.SmartRecommendations
                                       .Include("Product1") // Product1 là Sản phẩm B (gợi ý)
                                       .Where(r => r.Product.ProductID == id)
                                       .ToList();

            // 3. Phân loại theo 2 góc nhìn thuật toán để Admin dễ kiểm soát
            var model = new WebBanHang.Models.ViewModel.ProductAIDetailVM
            {
                ProductInfo = product,

                // Góc nhìn Two-Phase: Sắp xếp theo Lợi nhuận (ActualUtility) giảm dần
                TwoPhaseRecommendations = rawRecommendations
                                            .OrderByDescending(r => r.ActualUtility)
                                            .ToList(),

                // Góc nhìn Apriori: Sắp xếp theo Độ tin cậy (Confidence) giảm dần, rồi đến Tần suất
                AprioriRecommendations = rawRecommendations
                                            .OrderByDescending(r => r.Confidence)
                                            .ThenByDescending(r => r.Support)
                                            .ToList()
            };

            return View(model);
        }
    }
}