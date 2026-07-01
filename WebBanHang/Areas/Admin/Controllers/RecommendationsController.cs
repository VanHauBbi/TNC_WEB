using System;
using System.Linq;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Services;

namespace WebBanHang.Areas.Admin.Controllers
{
    public class RecommendationsController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();
        private AprioriService _aprioriService = new AprioriService();

        // GET: Admin/Recommendations
        public ActionResult Index()
        {
            // Kéo danh sách các quy tắc đã được tạo ra
            // Chú ý: "Product" và "Product1" là tên Navigation Property mặc định do Entity Framework tự sinh ra
            var rules = db.ProductRecommendations
                          .Include("Product")  // Gọi thông tin Sản phẩm A
                          .Include("Product1") // Gọi thông tin Sản phẩm B
                          .OrderByDescending(r => r.Confidence)
                          .ToList();

            return View(rules);
        }

        // POST: Admin/Recommendations/RunAlgorithm
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RunAlgorithm()
        {
            try
            {
                // Chỉ truyền 1 tham số MinConfidence = 0.2 (Tương đương tỷ lệ mua chung đạt từ 20% trở lên)
                _aprioriService.RunAprioriAlgorithm(0.2);

                TempData["SuccessMessage"] = "Khai phá dữ liệu thành công! Hệ thống TNC đã cập nhật các bộ luật gợi ý mới nhất.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi trong quá trình chạy thuật toán: " + ex.Message;
            }

            // Chạy xong thì load lại trang Index
            return RedirectToAction("Index");
        }
    }
}