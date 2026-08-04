using System;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Services;

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
                ViewBag.SchemaError = "Chưa thể đọc dữ liệu marketing. Hãy chạy DatabaseScripts/MarketingSelling.sql. Chi tiết: " + ex.Message;
                return View(new WebBanHang.Models.ViewModel.MarketingDashboardVM());
            }
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
