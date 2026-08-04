using System;
using System.Globalization;
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
        public ActionResult SaveSettings(FormCollection form)
        {
            var settings = ReadSettings(form);
            if (!ModelState.IsValid)
            {
                try
                {
                    var dashboard = new MarketingSellingService(db).GetDashboard();
                    dashboard.Settings = settings ?? new MarketingSettingsVM();
                    return View("Index", dashboard);
                }
                catch (Exception ex)
                {
                    ViewBag.SchemaError = ex.Message;
                    return View("Index", new MarketingDashboardVM { Settings = settings ?? new MarketingSettingsVM() });
                }
            }
            try
            {
                var comboSettingsChanged = new MarketingSellingService(db).SaveSettings(settings, Session["UserName"] as string);
                TempData["SuccessMessage"] = comboSettingsChanged
                    ? "Đã lưu cấu hình. Các combo theo cấu hình cũ đã được ngừng; hãy bấm Tạo combo mua chung để sinh lại."
                    : "Đã lưu cấu hình Marketing Selling.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        private MarketingSettingsVM ReadSettings(FormCollection form)
        {
            var settings = new MarketingSettingsVM();

            ReadDecimal(form, "PersonalDiscountPct", "Tỷ lệ giảm voucher", value => settings.PersonalDiscountPct = value);
            ReadDecimal(form, "PersonalMaxDiscountAmount", "Số tiền giảm voucher tối đa", value => settings.PersonalMaxDiscountAmount = value);
            ReadDecimal(form, "PersonalMinInterestScore", "Điểm quan tâm tối thiểu", value => settings.PersonalMinInterestScore = value);
            ReadInt(form, "VoucherValidityHours", "Thời gian sử dụng voucher", value => settings.VoucherValidityHours = value);
            ReadInt(form, "VoucherCooldownDays", "Thời gian chờ phát lại voucher", value => settings.VoucherCooldownDays = value);
            ReadDecimal(form, "PersonalMinimumMarginPct", "Lợi nhuận voucher phải giữ lại", value => settings.PersonalMinimumMarginPct = value);

            ReadDecimal(form, "ComboDiscountPct", "Tỷ lệ giảm combo", value => settings.ComboDiscountPct = value);
            ReadDecimal(form, "ComboMaxDiscountAmount", "Mức giảm combo tối đa", value => settings.ComboMaxDiscountAmount = value);
            ReadInt(form, "ComboMinSupport", "Số đơn mua chung tối thiểu", value => settings.ComboMinSupport = value);
            ReadDecimal(form, "ComboMinConfidencePercent", "Tỷ lệ khách mua kèm tối thiểu", value => settings.ComboMinConfidencePercent = value);
            ReadDecimal(form, "ComboMinUtility", "Lợi nhuận trung bình của cặp", value => settings.ComboMinUtility = value);
            ReadInt(form, "ComboValidityDays", "Thời gian hoạt động combo", value => settings.ComboValidityDays = value);
            ReadInt(form, "ComboUsageLimit", "Số đơn sử dụng combo tối đa", value => settings.ComboUsageLimit = value);
            ReadDecimal(form, "ComboMinimumMarginPct", "Lợi nhuận combo phải giữ lại", value => settings.ComboMinimumMarginPct = value);

            TryValidateModel(settings);
            return settings;
        }

        private void ReadDecimal(FormCollection form, string key, string label, Action<decimal> assign)
        {
            var raw = (form[key] ?? string.Empty).Trim().Replace(" ", string.Empty);
            // Ô number gửi dấu chấm nhưng máy Việt Nam thường dùng dấu phẩy.
            // Dữ liệu cấu hình không dùng dấu phân cách hàng nghìn nên có thể chuẩn hóa trực tiếp.
            raw = raw.Replace(',', '.');
            decimal value;
            if (decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                 CultureInfo.InvariantCulture, out value))
            {
                assign(value);
                return;
            }
            ModelState.AddModelError(key, label + " phải là một số hợp lệ.");
        }

        private void ReadInt(FormCollection form, string key, string label, Action<int> assign)
        {
            int value;
            if (int.TryParse((form[key] ?? string.Empty).Trim(), NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out value))
            {
                assign(value);
                return;
            }
            ModelState.AddModelError(key, label + " phải là số nguyên hợp lệ.");
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
                TempData["SuccessMessage"] = "Đã tạo " + count + " combo mua chung mới. Mỗi combo chỉ giảm một lần trên tổng đơn.";
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
