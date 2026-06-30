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
        // Bổ sung các trường chi tiết để phục vụ khi bấm vào Card
        public int PendingOrders { get; set; } // Chờ xác nhận
        public int FailedOrders { get; set; }  // Đã hủy/Thất bại
        public decimal TotalShippingFee { get; set; } // Doanh thu từ phí vận chuyển
        public decimal TotalVoucherDiscount { get; set; } // Tổng tiền giảm giá (voucher)
    }
}