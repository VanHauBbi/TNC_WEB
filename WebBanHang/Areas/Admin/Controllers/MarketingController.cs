using System;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Services;
using WebBanHang.Models.ViewModel;

namespace WebBanHang.Areas.Admin.Controllers
{
    public class MarketingController : Controller
    {
        private readonly MyStoreEntities db = new MyStoreEntities();

        public ActionResult Index()
        {
            try
            {
                return View(new MarketingSellingService(db).GetDashboard());
            }
            catch (Exception ex)
            {
                ViewBag.SchemaError = "Chưa thể đọc dữ liệu marketing. Hãy cập nhật schema Marketing Selling trong database. Chi tiết: " + ex.Message;
                return View(new WebBanHang.Models.ViewModel.MarketingDashboardVM());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SaveSettings(MarketingSettingsVM settings)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Cấu hình không hợp lệ. Vui lòng kiểm tra lại các giới hạn.";
                return RedirectToAction("Index");
            }
            try
            {
                new MarketingSellingService(db).SaveSettings(settings, Session["UserName"] as string);
                TempData["SuccessMessage"] = "Đã lưu cấu hình Marketing Selling.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GeneratePersonalVouchers()
        {
            try
            {
                var count = new MarketingSellingService(db).GeneratePersonalVouchers(Session["UserName"] as string);
                TempData["SuccessMessage"] = "Đã tạo " + count + " voucher cá nhân mới.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GenerateComboOffers()
        {
            try
            {
                var count = new MarketingSellingService(db).GenerateComboOffers(Session["UserName"] as string);
                TempData["SuccessMessage"] = "Đã tạo " + count + " combo mới từ luật Hybrid.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
