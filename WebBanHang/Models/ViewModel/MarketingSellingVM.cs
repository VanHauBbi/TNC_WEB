using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WebBanHang.Models.ViewModel
{
    public class PersonalVoucherVM
    {
        public int CustomerCouponID { get; set; }
        public int CouponID { get; set; }
        public int? TargetProductID { get; set; }
        public string CouponName { get; set; }
        public string Code { get; set; }
        public string ProductName { get; set; }
        public string ProductImage { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal InterestScore { get; set; }
        public string TriggerType { get; set; }
        public string Status { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class ComboOfferVM
    {
        public int ComboOfferID { get; set; }
        public int ProductID_A { get; set; }
        public int ProductID_B { get; set; }
        public string ProductNameA { get; set; }
        public string ProductNameB { get; set; }
        public string ProductImageA { get; set; }
        public string ProductImageB { get; set; }
        public decimal ProductPriceA { get; set; }
        public decimal ProductPriceB { get; set; }
        public string Code { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal OriginalAmount => ProductPriceA + ProductPriceB;
        public decimal DiscountedAmount => Math.Max(0m, OriginalAmount - DiscountAmount);
        public int Support { get; set; }
        public decimal Confidence { get; set; }
        public decimal ActualUtility { get; set; }
        public decimal MinimumMarginPct { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class MarketingDashboardVM
    {
        public int ActivePersonalVouchers { get; set; }
        public int ActiveComboOffers { get; set; }
        public int RedeemedVouchers { get; set; }
        public decimal RevenueInfluenced { get; set; }
        public MarketingSettingsVM Settings { get; set; } = new MarketingSettingsVM();
        public List<PersonalVoucherVM> RecentPersonalVouchers { get; set; } = new List<PersonalVoucherVM>();
        public List<ComboOfferVM> ActiveCombos { get; set; } = new List<ComboOfferVM>();
        public List<ComboOfferAdminVM> ManagedCombos { get; set; } = new List<ComboOfferAdminVM>();
    }

    public class MarketingSettingsVM
    {
        [Range(0.01, 100)] public decimal PersonalDiscountPct { get; set; } = 5m;
        [Range(1000, 100000000)] public decimal PersonalMaxDiscountAmount { get; set; } = 150000m;
        [Range(0, 100000)] public decimal PersonalMinInterestScore { get; set; } = 8m;
        [Range(1, 720)] public int VoucherValidityHours { get; set; } = 72;
        [Range(0, 365)] public int VoucherCooldownDays { get; set; } = 7;
        [Range(0, 100)] public decimal PersonalMinimumMarginPct { get; set; } = 5m;

        [Range(0.01, 100)] public decimal ComboDiscountPct { get; set; } = 5m;
        [Range(1000, 100000000)] public decimal ComboMaxDiscountAmount { get; set; } = 300000m;
        [Range(1, int.MaxValue)] public int ComboMinSupport { get; set; } = 2;
        [Range(0, 100)] public decimal ComboMinConfidencePercent { get; set; } = 20m;
        [Range(0, 1000000000)] public decimal ComboMinUtility { get; set; } = 100000m;
        [Range(1, 365)] public int ComboValidityDays { get; set; } = 14;
        [Range(1, 1000000)] public int ComboUsageLimit { get; set; } = 100;
        [Range(0, 100)] public decimal ComboMinimumMarginPct { get; set; } = 5m;
    }

    public class PromotionEvaluation
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public string PromotionType { get; set; }
        public string Code { get; set; }
        public decimal DiscountAmount { get; set; }
        public int? CouponID { get; set; }
        public int? CustomerCouponID { get; set; }
        public int? ComboOfferID { get; set; }
        public int? ProductID_A { get; set; }
        public int? ProductID_B { get; set; }
        public string ProductNameA { get; set; }
        public string ProductNameB { get; set; }
        public decimal OriginalAmount { get; set; }
        public decimal FinalAmount => Math.Max(0m, OriginalAmount - DiscountAmount);
    }

    public class PersonalVoucherAdminVM
    {
        public int CustomerCouponID { get; set; }
        public int CouponID { get; set; }
        public int CustomerID { get; set; }
        public int? TargetProductID { get; set; }
        public string CustomerName { get; set; }
        public string CustomerEmail { get; set; }
        public string ProductName { get; set; }
        public string ProductImage { get; set; }
        public string CouponName { get; set; }
        public string Code { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal InterestScore { get; set; }
        public string TriggerType { get; set; }
        public string Status { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? ViewedAt { get; set; }
        public DateTime? ClickedAt { get; set; }
        public DateTime? AddedToCartAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int? OrderID { get; set; }
        public int UsageLimit { get; set; }
        public decimal MinimumMarginPct { get; set; }
        public bool CouponIsActive { get; set; }
    }

    public class PersonalVoucherEditVM
    {
        public int CustomerCouponID { get; set; }
        public string Code { get; set; }
        public string CustomerName { get; set; }
        public string ProductName { get; set; }

        [Display(Name = "Số tiền giảm")]
        [Range(1000, 100000000, ErrorMessage = "Số tiền giảm phải từ 1.000 ₫ trở lên.")]
        public decimal DiscountAmount { get; set; }

        [Display(Name = "Hạn sử dụng")]
        [Required(ErrorMessage = "Vui lòng chọn hạn sử dụng.")]
        public DateTime ExpiresAt { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsActive { get; set; }
    }

    public class ComboOfferAdminVM
    {
        public int ComboOfferID { get; set; }
        public int ProductID_A { get; set; }
        public int ProductID_B { get; set; }
        public string ProductNameA { get; set; }
        public string ProductNameB { get; set; }
        public string ProductImageA { get; set; }
        public string ProductImageB { get; set; }
        public decimal ProductPriceA { get; set; }
        public decimal ProductPriceB { get; set; }
        public string Code { get; set; }
        public decimal DiscountAmount { get; set; }
        public int Support { get; set; }
        public decimal Confidence { get; set; }
        public decimal ActualUtility { get; set; }
        public decimal MinimumMarginPct { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int UsageLimit { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UsedOrderCount { get; set; }
        public decimal UsedDiscountTotal { get; set; }
        public decimal OriginalAmount => ProductPriceA + ProductPriceB;
        public decimal DiscountedAmount => Math.Max(0m, OriginalAmount - DiscountAmount);
    }

    public class ComboOfferEditVM
    {
        public int ComboOfferID { get; set; }
        public string Code { get; set; }
        public string ProductNameA { get; set; }
        public string ProductNameB { get; set; }

        [Display(Name = "Số tiền giảm cho cả combo")]
        [Range(1000, 100000000, ErrorMessage = "Số tiền giảm phải từ 1.000 ₫ trở lên.")]
        public decimal DiscountAmount { get; set; }

        [Display(Name = "Ngày bắt đầu")]
        [Required(ErrorMessage = "Vui lòng chọn ngày bắt đầu.")]
        public DateTime StartDate { get; set; }

        [Display(Name = "Ngày kết thúc")]
        [Required(ErrorMessage = "Vui lòng chọn ngày kết thúc.")]
        public DateTime EndDate { get; set; }

        [Display(Name = "Lượt sử dụng còn lại")]
        [Range(0, 1000000, ErrorMessage = "Lượt sử dụng không được âm.")]
        public int UsageLimit { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsActive { get; set; }
    }
}
