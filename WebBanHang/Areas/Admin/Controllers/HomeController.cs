using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Models.ViewModel;

namespace WebBanHang.Areas.Admin.Controllers
{
    public class HomeController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();
        private IQueryable<Order> GetValidOrders(DateTime fromDate, DateTime toDate)
        {
            return db.Orders.Where(o => o.OrderDate >= fromDate && o.OrderDate <= toDate
                   && (o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành"));
        }
        private AdminDashboardVM GetDashboardStatistics(DateTime? fromDate, DateTime? toDate)
        {
            var query = db.Orders.AsQueryable();
            if (fromDate.HasValue && toDate.HasValue)
                query = query.Where(o => o.OrderDate >= fromDate && o.OrderDate <= toDate);

            var orders = query.Include("OrderDetails.Product").ToList();

            var vm = new AdminDashboardVM();
            vm.TotalOrders = orders.Count;

            // Lọc các đơn hàng thành công
            var successOrders = orders.Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao").ToList();

            // DOANH THU THỰC: Chỉ tính tiền hàng, KHÔNG TÍNH PHÍ SHIP
            vm.TotalRevenue = successOrders.Sum(o => o.OrderDetails.Sum(d => d.UnitPrice * d.Quantity));

            // CHI PHÍ: Giá vốn (COGS) + Phí vận chuyển phải trả cho bên thứ 3
            decimal totalCOGS = successOrders.SelectMany(o => o.OrderDetails).Sum(d => d.Quantity * d.ImportPrice);
            decimal totalShippingFee = 0;

            foreach (var o in successOrders)
            {
                if (o.ShippingMethod == "Giao hàng nhanh") totalShippingFee += 30000;
                else if (o.ShippingMethod == "Giao hàng tiết kiệm") totalShippingFee += 15000;
            }

            // LỢI NHUẬN THỰC = Tổng tiền khách trả - (Tổng phí ship khách trả - Phí ship thực trả) - Giá vốn
            // Tuy nhiên, để đơn giản và chuẩn xác: Lợi nhuận = Doanh thu thuần - Giá vốn - Phí ship thực tế
            vm.TotalProfit = vm.TotalRevenue - totalCOGS - totalShippingFee;

            return vm;
        }

        public ActionResult Index()
        {
            // Gọi hàm GetDashboardStatistics để lấy số liệu tổng quát
            var dashboardVM = GetDashboardStatistics(null, null);

            // Bổ sung các dữ liệu đặc thù (Không nằm trong hàm tính tổng)
            dashboardVM.LowStockProducts = db.Products.Where(p => p.StockQuantity < 10).OrderBy(p => p.StockQuantity).Take(5).ToList();
            dashboardVM.LowStockProductCount = db.Products.Count(p => p.StockQuantity < 10);
            dashboardVM.TopSellingProducts = db.Products
                .OrderByDescending(p => p.OrderDetails.Sum(od => (int?)od.Quantity) ?? 0)
                .Take(5).ToList();
            //// 2. THỐNG KÊ DOANH THU & LỢI NHUẬN
            //dashboardVM.TotalRevenue = 0;
            //dashboardVM.TotalProfit = 0;

            //// Nới lỏng điều kiện: Tính cả những đơn "Đã duyệt" để dễ Demo hiển thị số liệu
            //var validOrders = allOrders.Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt").ToList();

            //foreach (var order in validOrders)
            //{
            //    // Cộng dồn Doanh thu
            //    dashboardVM.TotalRevenue += order.TotalAmount;

            //    // Trích xuất Phí vận chuyển
            //    decimal shippingFee = 0;
            //    if (order.ShippingMethod == "Giao hàng nhanh") shippingFee = 30000;
            //    else if (order.ShippingMethod == "Giao hàng tiết kiệm") shippingFee = 15000;

            //    // Tính Tổng vốn nhập hàng (COGS) của đơn này
            //    decimal costOfGoods = 0;
            //    if (order.OrderDetails != null)
            //    {
            //        foreach (var detail in order.OrderDetails)
            //        {
            //            // Lấy ImportPrice từ DB
            //            decimal importPrice = detail.Product != null ? detail.Product.ImportPrice : 0;
            //            costOfGoods += (importPrice * detail.Quantity);
            //        }
            //    }

            //    // Cộng dồn Lợi nhuận = Tiền khách trả - Phí Ship - Vốn nhập
            //    dashboardVM.TotalProfit += (order.TotalAmount - shippingFee - costOfGoods);
            //}

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

        [HttpPost]
        public JsonResult GetDashboardData(DateTime fromDate, DateTime toDate)
        {
            var orders = GetValidOrders(fromDate, toDate).ToList();

            decimal totalRevenue = orders.Sum(o => o.TotalAmount);

            // Tính tổng vốn và tổng phí ship
            decimal totalCost = 0;
            decimal totalShipping = 0;

            foreach (var o in orders)
            {
                // 1. Tính vốn
                totalCost += o.OrderDetails.Sum(d => d.Quantity * d.ImportPrice);

                // 2. Tính phí ship đồng bộ với Index()
                if (o.ShippingMethod == "Giao hàng nhanh") totalShipping += 30000;
                else if (o.ShippingMethod == "Giao hàng tiết kiệm") totalShipping += 15000;
            }

            // Lợi nhuận = Doanh thu - Vốn - Phí ship
            decimal grossProfit = totalRevenue - totalCost - totalShipping;

            return Json(new
            {
                totalRevenue = totalRevenue.ToString("N0") + " ₫",
                totalProfit = grossProfit.ToString("N0") + " ₫",
                orderCount = orders.Count,
                chartLabels = orders.GroupBy(o => o.OrderDate.Date).Select(g => g.Key.ToString("dd/MM")).ToList(),
                chartData = orders.GroupBy(o => o.OrderDate.Date).Select(g => (double)g.Sum(o => o.TotalAmount)).ToList()
            });
        }
    }
}