using System;
using System.Configuration;
using System.Net;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Services;

namespace WebBanHang.Controllers
{
    // Endpoint dành cho Windows Task Scheduler/Hangfire gọi theo lịch.
    // Chỉ hoạt động khi MarketingSchedulerKey đã được cấu hình ở môi trường chạy thật.
    public class MarketingJobsController : Controller
    {
        [HttpPost]
        public ActionResult RunDaily(string key)
        {
            var configuredKey = ConfigurationManager.AppSettings["MarketingSchedulerKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
                return new HttpStatusCodeResult(HttpStatusCode.ServiceUnavailable, "MarketingSchedulerKey chưa được cấu hình.");
            if (!string.Equals(configuredKey, key, StringComparison.Ordinal))
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            using (var db = new MyStoreEntities())
            {
                var service = new MarketingSellingService(db);
                var vouchers = service.GeneratePersonalVouchers("SCHEDULER");
                var combos = service.GenerateComboOffers("SCHEDULER");
                return Json(new { success = true, personalVouchersCreated = vouchers, comboOffersCreated = combos });
            }
        }
    }
}
