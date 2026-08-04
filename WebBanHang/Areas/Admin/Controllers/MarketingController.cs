using System;
using System.Globalization;
using System.Net;
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

            if (settings.PersonalMinimumMarginPct != 10m && settings.PersonalMinimumMarginPct != 5m && settings.PersonalMinimumMarginPct != 2m)
                ModelState.AddModelError("PersonalMinimumMarginPct", "Vui lòng chọn một chế độ bảo vệ lợi nhuận voucher.");
            if (settings.ComboMinimumMarginPct != 10m && settings.ComboMinimumMarginPct != 5m && settings.ComboMinimumMarginPct != 2m)
                ModelState.AddModelError("ComboMinimumMarginPct", "Vui lòng chọn một chế độ bảo vệ lợi nhuận combo.");

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

        public ActionResult PersonalVoucherDetails(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var model = new MarketingSellingService(db).GetPersonalVoucherAdmin(id.Value);
            if (model == null) return HttpNotFound();
            return View(model);
        }

        public ActionResult EditPersonalVoucher(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var model = new MarketingSellingService(db).GetPersonalVoucherEdit(id.Value);
            if (model == null) return HttpNotFound();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditPersonalVoucher(PersonalVoucherEditVM model)
        {
            if (!ModelState.IsValid) return View(model);
            try
            {
                var savedDiscount = new MarketingSellingService(db).UpdatePersonalVoucher(model);
                TempData["SuccessMessage"] = savedDiscount < model.DiscountAmount
                    ? "Đã lưu voucher. Hệ thống tự hạ mức giảm còn " + savedDiscount.ToString("N0") + " ₫ để bảo vệ lợi nhuận FIFO."
                    : "Đã cập nhật voucher cá nhân.";
                return RedirectToAction("PersonalVoucherDetails", new { id = model.CustomerCouponID });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
            }
        }

        public ActionResult DeletePersonalVoucher(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var model = new MarketingSellingService(db).GetPersonalVoucherAdmin(id.Value);
            if (model == null) return HttpNotFound();
            return View(model);
        }

        [HttpPost, ActionName("DeletePersonalVoucher")]
        [ValidateAntiForgeryToken]
        public ActionResult DeletePersonalVoucherConfirmed(int id)
        {
            try
            {
                new MarketingSellingService(db).DeactivatePersonalVoucher(id);
                TempData["SuccessMessage"] = "Voucher đã được ngừng áp dụng; lịch sử vẫn được giữ nguyên.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        public ActionResult ComboDetails(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var model = new MarketingSellingService(db).GetComboOfferAdmin(id.Value);
            if (model == null) return HttpNotFound();
            return View(model);
        }

        public ActionResult EditCombo(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var model = new MarketingSellingService(db).GetComboOfferEdit(id.Value);
            if (model == null) return HttpNotFound();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditCombo(ComboOfferEditVM model)
        {
            if (!ModelState.IsValid) return View(model);
            try
            {
                var savedDiscount = new MarketingSellingService(db).UpdateComboOffer(model);
                TempData["SuccessMessage"] = savedDiscount < model.DiscountAmount
                    ? "Đã lưu combo. Hệ thống tự hạ mức giảm còn " + savedDiscount.ToString("N0") + " ₫ để bảo vệ lợi nhuận FIFO."
                    : "Đã cập nhật combo mua chung.";
                return RedirectToAction("ComboDetails", new { id = model.ComboOfferID });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
            }
        }

        public ActionResult DeleteCombo(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var model = new MarketingSellingService(db).GetComboOfferAdmin(id.Value);
            if (model == null) return HttpNotFound();
            return View(model);
        }

        [HttpPost, ActionName("DeleteCombo")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteComboConfirmed(int id)
        {
            try
            {
                new MarketingSellingService(db).DeactivateComboOffer(id);
                TempData["SuccessMessage"] = "Combo đã được ngừng áp dụng; lịch sử vẫn được giữ nguyên.";
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
