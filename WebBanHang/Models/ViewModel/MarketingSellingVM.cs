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
        [Range(0, 1)] public decimal ComboMinConfidence { get; set; } = 0.20m;
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
}
