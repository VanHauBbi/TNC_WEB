using System;
using System.Collections.Generic;
using WebBanHang.Models;

namespace WebBanHang.Models.ViewModel
{
    public class AdminDashboardVM
    {
        public int TotalOrders { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalProfit { get; set; }
        public int SuccessOrders { get; set; }
        public int CancelledOrders { get; set; }
        public int LowStockProductCount { get; set; }

        public List<Product> TopSellingProducts { get; set; }
        public List<Product> LowStockProducts { get; set; }

        // Dữ liệu cho Biểu đồ Chart.js
        public List<string> ChartLabels { get; set; }
        public List<decimal> ChartData { get; set; }

        public int PendingOrders { get; set; }
        public int FailedOrders { get; set; }
        public decimal TotalShippingFee { get; set; }
        public decimal TotalVoucherDiscount { get; set; }

        // BỔ SUNG BIẾN NÀY ĐỂ CHỨA DỮ LIỆU TIMELINE
        public List<TimelineItemVM> RecentActivities { get; set; }
    }

    // THÊM CLASS NÀY XUỐNG DƯỚI CÙNG ĐỂ KHAI BÁO CẤU TRÚC TIMELINE
    public class TimelineItemVM
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public string IconClass { get; set; }
        public string IconColorClass { get; set; }
        public string BadgeText { get; set; }
        public string ActionUrl { get; set; }
    }
}