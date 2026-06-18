using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Models.ViewModel;

namespace WebBanHang.Areas.Admin.Controllers
{
    public class HomeController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        public ActionResult Index()
        {
            var dashboardVM = new AdminDashboardVM();
            // Lấy toàn bộ đơn hàng có kèm chi tiết
            var allOrders = db.Orders.Include("OrderDetails.Product").ToList();

            // 1. PHỤC HỒI THỐNG KÊ TỔNG QUAN (Sửa lỗi 0 đơn hàng)
            dashboardVM.TotalOrders = allOrders.Count;
            dashboardVM.SuccessOrders = allOrders.Count(o => o.OrderStatus == "Đã duyệt" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành");
            dashboardVM.CancelledOrders = allOrders.Count(o => o.OrderStatus == "Đã hủy");

            // 2. THỐNG KÊ DOANH THU & LỢI NHUẬN
            dashboardVM.TotalRevenue = 0;
            dashboardVM.TotalProfit = 0;

            // Nới lỏng điều kiện: Tính cả những đơn "Đã duyệt" để dễ Demo hiển thị số liệu
            var validOrders = allOrders.Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt").ToList();

            foreach (var order in validOrders)
            {
                // Cộng dồn Doanh thu
                dashboardVM.TotalRevenue += order.TotalAmount;

                // Trích xuất Phí vận chuyển
                decimal shippingFee = 0;
                if (order.ShippingMethod == "Giao hàng nhanh") shippingFee = 30000;
                else if (order.ShippingMethod == "Giao hàng tiết kiệm") shippingFee = 15000;

                // Tính Tổng vốn nhập hàng (COGS) của đơn này
                decimal costOfGoods = 0;
                if (order.OrderDetails != null)
                {
                    foreach (var detail in order.OrderDetails)
                    {
                        // Lấy ImportPrice từ DB
                        decimal importPrice = detail.Product != null ? detail.Product.ImportPrice : 0;
                        costOfGoods += (importPrice * detail.Quantity);
                    }
                }

                // Cộng dồn Lợi nhuận = Tiền khách trả - Phí Ship - Vốn nhập
                dashboardVM.TotalProfit += (order.TotalAmount - shippingFee - costOfGoods);
            }

            // 3. CẢNH BÁO TỒN KHO
            dashboardVM.LowStockProducts = db.Products.Where(p => p.StockQuantity < 10).OrderBy(p => p.StockQuantity).Take(5).ToList();
            dashboardVM.LowStockProductCount = db.Products.Count(p => p.StockQuantity < 10);

            // 4. TOP SẢN PHẨM BÁN CHẠY
            dashboardVM.TopSellingProducts = db.Products
                .OrderByDescending(p => p.OrderDetails.Sum(od => (int?)od.Quantity) ?? 0)
                .Take(5).ToList();

            // 5. BIỂU ĐỒ DOANH THU (6 THÁNG)
            var sixMonthsAgo = DateTime.Now.AddMonths(-5);
            var monthlyRevenue = db.Orders
                .Where(o => o.OrderDate >= sixMonthsAgo && (o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt"))
                .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new {
                    Month = g.Key.Month + "/" + g.Key.Year,
                    Revenue = g.Sum(o => o.TotalAmount)
                }).ToList();

            dashboardVM.ChartLabels = monthlyRevenue.Select(m => m.Month).ToList();
            dashboardVM.ChartData = monthlyRevenue.Select(m => m.Revenue).ToList();

            return View(dashboardVM);
        }
    }
}