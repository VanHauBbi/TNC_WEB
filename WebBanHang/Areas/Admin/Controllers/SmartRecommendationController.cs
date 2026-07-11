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
    }
}