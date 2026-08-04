using System;
using System.Collections.Generic;

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
        public string ProductImageB { get; set; }
        public string Code { get; set; }
        public decimal DiscountAmount { get; set; }
        public int Support { get; set; }
        public decimal Confidence { get; set; }
        public decimal ActualUtility { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class MarketingDashboardVM
    {
        public int ActivePersonalVouchers { get; set; }
        public int ActiveComboOffers { get; set; }
        public int RedeemedVouchers { get; set; }
        public decimal RevenueInfluenced { get; set; }
        public List<PersonalVoucherVM> RecentPersonalVouchers { get; set; } = new List<PersonalVoucherVM>();
        public List<ComboOfferVM> ActiveCombos { get; set; } = new List<ComboOfferVM>();
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
    }
}
