//using Newtonsoft.Json;
//using System;
//using System.Collections.Generic;
//using System.Data.Entity;
//using System.Linq;
//using System.Net.Http;
//using System.Threading.Tasks;
//using System.Web.Mvc;
//using WebBanHang.Models;
//using WebBanHang.Models.ViewModel;

//namespace WebBanHang.Areas.Admin.Controllers
//{
//    public class HomeController : Controller
//    {
//        private MyStoreEntities db = new MyStoreEntities();
//        private IQueryable<Order> GetValidOrders(DateTime fromDate, DateTime toDate)
//        {
//            return db.Orders.Where(o => o.OrderDate >= fromDate && o.OrderDate <= toDate
//                   && (o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành"));
//        }
//        private AdminDashboardVM GetDashboardStatistics(DateTime? fromDate, DateTime? toDate)
//        {
//            var query = db.Orders.AsQueryable();
//            if (fromDate.HasValue && toDate.HasValue)
//                query = query.Where(o => o.OrderDate >= fromDate && o.OrderDate <= toDate);

//            var orders = query.Include("OrderDetails.Product").ToList();

//            var vm = new AdminDashboardVM();
//            vm.TotalOrders = orders.Count;

//            // Lọc các đơn hàng thành công
//            var successOrders = orders.Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao").ToList();

//            // DOANH THU THỰC: Chỉ tính tiền hàng, KHÔNG TÍNH PHÍ SHIP
//            vm.TotalRevenue = successOrders.Sum(o => o.OrderDetails.Sum(d => d.UnitPrice * d.Quantity));

//            // CHI PHÍ: Giá vốn (COGS) + Phí vận chuyển phải trả cho bên thứ 3
//            decimal totalCOGS = successOrders.SelectMany(o => o.OrderDetails).Sum(d => d.Quantity * d.ImportPrice);
//            decimal totalShippingFee = 0;

//            foreach (var o in successOrders)
//            {
//                if (o.ShippingMethod == "Giao hàng nhanh") totalShippingFee += 30000;
//                else if (o.ShippingMethod == "Giao hàng tiết kiệm") totalShippingFee += 15000;
//            }

//            // LỢI NHUẬN THỰC = Tổng tiền khách trả - (Tổng phí ship khách trả - Phí ship thực trả) - Giá vốn
//            // Tuy nhiên, để đơn giản và chuẩn xác: Lợi nhuận = Doanh thu thuần - Giá vốn - Phí ship thực tế
//            vm.TotalProfit = vm.TotalRevenue - totalCOGS - totalShippingFee;

//            return vm;
//        }

//        public ActionResult Index()
//        {
//            // Gọi hàm GetDashboardStatistics để lấy số liệu tổng quát
//            var dashboardVM = GetDashboardStatistics(null, null);

//            // Bổ sung các dữ liệu đặc thù (Không nằm trong hàm tính tổng)
//            dashboardVM.LowStockProducts = db.Products.Where(p => p.StockQuantity < 10).OrderBy(p => p.StockQuantity).Take(5).ToList();
//            dashboardVM.LowStockProductCount = db.Products.Count(p => p.StockQuantity < 10);
//            dashboardVM.TopSellingProducts = db.Products
//                .OrderByDescending(p => p.OrderDetails.Sum(od => (int?)od.Quantity) ?? 0)
//                .Take(5).ToList();
//            //// 2. THỐNG KÊ DOANH THU & LỢI NHUẬN
//            //dashboardVM.TotalRevenue = 0;
//            //dashboardVM.TotalProfit = 0;

//            //// Nới lỏng điều kiện: Tính cả những đơn "Đã duyệt" để dễ Demo hiển thị số liệu
//            //var validOrders = allOrders.Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt").ToList();

//            //foreach (var order in validOrders)
//            //{
//            //    // Cộng dồn Doanh thu
//            //    dashboardVM.TotalRevenue += order.TotalAmount;

//            //    // Trích xuất Phí vận chuyển
//            //    decimal shippingFee = 0;
//            //    if (order.ShippingMethod == "Giao hàng nhanh") shippingFee = 30000;
//            //    else if (order.ShippingMethod == "Giao hàng tiết kiệm") shippingFee = 15000;

//            //    // Tính Tổng vốn nhập hàng (COGS) của đơn này
//            //    decimal costOfGoods = 0;
//            //    if (order.OrderDetails != null)
//            //    {
//            //        foreach (var detail in order.OrderDetails)
//            //        {
//            //            // Lấy ImportPrice từ DB
//            //            decimal importPrice = detail.Product != null ? detail.Product.ImportPrice : 0;
//            //            costOfGoods += (importPrice * detail.Quantity);
//            //        }
//            //    }

//            //    // Cộng dồn Lợi nhuận = Tiền khách trả - Phí Ship - Vốn nhập
//            //    dashboardVM.TotalProfit += (order.TotalAmount - shippingFee - costOfGoods);
//            //}

//            // 3. CẢNH BÁO TỒN KHO
//            dashboardVM.LowStockProducts = db.Products.Where(p => p.StockQuantity < 10).OrderBy(p => p.StockQuantity).Take(5).ToList();
//            dashboardVM.LowStockProductCount = db.Products.Count(p => p.StockQuantity < 10);

//            // 4. TOP SẢN PHẨM BÁN CHẠY
//            dashboardVM.TopSellingProducts = db.Products
//                .OrderByDescending(p => p.OrderDetails.Sum(od => (int?)od.Quantity) ?? 0)
//                .Take(5).ToList();

//            // 5. BIỂU ĐỒ DOANH THU (6 THÁNG)
//            var sixMonthsAgo = DateTime.Now.AddMonths(-5);
//            var monthlyRevenue = db.Orders
//                .Where(o => o.OrderDate >= sixMonthsAgo && (o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt"))
//                .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
//                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
//                .Select(g => new {
//                    Month = g.Key.Month + "/" + g.Key.Year,
//                    Revenue = g.Sum(o => o.TotalAmount)
//                }).ToList();

//            dashboardVM.ChartLabels = monthlyRevenue.Select(m => m.Month).ToList();
//            dashboardVM.ChartData = monthlyRevenue.Select(m => m.Revenue).ToList();

//            return View(dashboardVM);
//        }

//        [HttpPost]
//        public JsonResult GetDashboardData(DateTime fromDate, DateTime toDate)
//        {
//            var orders = GetValidOrders(fromDate, toDate).ToList();

//            decimal totalRevenue = orders.Sum(o => o.TotalAmount);

//            // Tính tổng vốn và tổng phí ship
//            decimal totalCost = 0;
//            decimal totalShipping = 0;

//            foreach (var o in orders)
//            {
//                // 1. Tính vốn
//                totalCost += o.OrderDetails.Sum(d => d.Quantity * d.ImportPrice);

//                // 2. Tính phí ship đồng bộ với Index()
//                if (o.ShippingMethod == "Giao hàng nhanh") totalShipping += 30000;
//                else if (o.ShippingMethod == "Giao hàng tiết kiệm") totalShipping += 15000;
//            }

//            // Lợi nhuận = Doanh thu - Vốn - Phí ship
//            decimal grossProfit = totalRevenue - totalCost - totalShipping;

//            return Json(new
//            {
//                totalRevenue = totalRevenue.ToString("N0") + " ₫",
//                totalProfit = grossProfit.ToString("N0") + " ₫",
//                orderCount = orders.Count,
//                chartLabels = orders.GroupBy(o => o.OrderDate.Date).Select(g => g.Key.ToString("dd/MM")).ToList(),
//                chartData = orders.GroupBy(o => o.OrderDate.Date).Select(g => (double)g.Sum(o => o.TotalAmount)).ToList()
//            });
//        }
//    }
//}

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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

            // ========================================================
            // 1. SỬA LỖI PIE CHART: Tính số lượng Đơn Thành Công / Đã Hủy
            // ========================================================
            var successOrders = orders.Where(o =>
                // Nếu là VNPay: Phải Thanh toán xong + Giao thành công
                (o.PaymentMethod == "VNPay" && o.PaymentStatus == "Đã thanh toán" && (o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành")) ||
                // Nếu không phải VNPay (COD/Tiền mặt): Chỉ cần Giao thành công
                (o.PaymentMethod != "VNPay" && (o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành"))
            ).ToList();

            // Gán dữ liệu cho View Model để vẽ Biểu đồ tròn
            vm.SuccessOrders = successOrders.Count;
            vm.CancelledOrders = orders.Count(o => o.OrderStatus == "Đã hủy" || o.OrderStatus == "Hủy đơn");

            vm.TotalRevenue = successOrders.Sum(o => o.OrderDetails.Sum(d => d.UnitPrice * d.Quantity));

            decimal totalCOGS = successOrders.SelectMany(o => o.OrderDetails).Sum(d => d.Quantity * d.ImportPrice);
            decimal totalShippingFee = 0;

            foreach (var o in successOrders)
            {
                if (o.ShippingMethod == "Giao hàng nhanh") totalShippingFee += 30000;
                else if (o.ShippingMethod == "Giao hàng tiết kiệm") totalShippingFee += 15000;
            }

            vm.TotalProfit = vm.TotalRevenue - totalCOGS - totalShippingFee;
            return vm;
        }

        public ActionResult Index()
        {
            var dashboardVM = GetDashboardStatistics(null, null);

            dashboardVM.LowStockProducts = db.Products.Where(p => p.StockQuantity < 10).OrderBy(p => p.StockQuantity).Take(5).ToList();
            dashboardVM.LowStockProductCount = db.Products.Count(p => p.StockQuantity < 10);

            dashboardVM.TopSellingProducts = db.Products
                .OrderByDescending(p => p.OrderDetails.Sum(od => (int?)od.Quantity) ?? 0)
                .Take(5).ToList();

            DateTime sixMonthsAgo = DateTime.Now.AddMonths(-5);
            var validOrders = db.Orders.Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt").ToList();

            DateTime earliestDate = validOrders.Any() ? validOrders.Min(o => o.OrderDate) : sixMonthsAgo;
            DateTime startDate = earliestDate < sixMonthsAgo ? earliestDate : sixMonthsAgo;

            DateTime tempDate = new DateTime(startDate.Year, startDate.Month, 1);
            DateTime endDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            var chartLabels = new List<string>();
            var chartDataList = new List<decimal>();

            while (tempDate <= endDate)
            {
                chartLabels.Add(tempDate.ToString("MM/yyyy"));
                var monthTotal = validOrders
                    .Where(o => o.OrderDate.Year == tempDate.Year && o.OrderDate.Month == tempDate.Month)
                    .Sum(o => o.TotalAmount);

                chartDataList.Add(monthTotal);
                tempDate = tempDate.AddMonths(1);
            }

            dashboardVM.ChartLabels = chartLabels;
            dashboardVM.ChartData = chartDataList;

            dashboardVM.RecentActivities = new List<TimelineItemVM>();

            var recentChats = db.SupportSessions.OrderByDescending(s => s.StartTime).Take(5).ToList();
            foreach (var chat in recentChats)
            {
                dashboardVM.RecentActivities.Add(new TimelineItemVM
                {
                    Title = "Yêu cầu hỗ trợ Chat",
                    Description = "Có khách hàng cần tư vấn.",
                    CreatedAt = chat.StartTime,
                    IconClass = "fa-message",
                    IconColorClass = "bg-info",
                    ActionUrl = Url.Action("Index", "AdminSupport")
                });
            }

            var recentOrders = db.Orders.OrderByDescending(o => o.OrderDate).Take(5).ToList();
            foreach (var order in recentOrders)
            {
                dashboardVM.RecentActivities.Add(new TimelineItemVM
                {
                    Title = "Đơn hàng mới #" + order.OrderID,
                    Description = "Trị giá đơn: " + order.TotalAmount.ToString("N0") + " ₫",
                    CreatedAt = order.OrderDate,
                    IconClass = "fa-cart-shopping",
                    IconColorClass = "bg-success",
                    ActionUrl = Url.Action("Details", "Orders", new { id = order.OrderID })
                });
            }

            dashboardVM.RecentActivities = dashboardVM.RecentActivities
                                        .OrderByDescending(x => x.CreatedAt)
                                        .Take(10).ToList();

            return View(dashboardVM);
        }

        [HttpPost]
        public JsonResult GetDashboardData(string fromDate, string toDate, string filterType)
        {
            // BƯỚC 1: LẤY TOÀN BỘ ĐƠN HÀNG (Cần Include OrderDetails để tính lợi nhuận y hệt hàm Index)
            var query = db.Orders.Include("OrderDetails.Product").AsQueryable();

            DateTime dtFrom = DateTime.Now;
            DateTime dtTo = DateTime.Now;
            bool isAllTime = (filterType == "all" || string.IsNullOrEmpty(fromDate) || fromDate == "2000-01-01");

            if (!isAllTime)
            {
                DateTime.TryParse(fromDate, out dtFrom);
                DateTime.TryParse(toDate, out dtTo);
                dtTo = dtTo.Date.AddDays(1).AddSeconds(-1);
                query = query.Where(o => o.OrderDate >= dtFrom && o.OrderDate <= dtTo);
            }

            var allOrders = query.ToList();

            // BƯỚC 2: ĐỒNG BỘ LOGIC TÍNH TIỀN (Khớp 100% với hàm GetDashboardStatistics lúc mới vào trang)
            var successOrders = allOrders.Where(o =>
                (o.PaymentMethod == "VNPay" && o.PaymentStatus == "Đã thanh toán" && (o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành")) ||
                (o.PaymentMethod != "VNPay" && (o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành"))
            ).ToList();

            // Tính tổng doanh thu dựa trên giá chi tiết từng sản phẩm
            decimal totalRevenue = successOrders.Sum(o => o.OrderDetails != null ? o.OrderDetails.Sum(d => d.UnitPrice * d.Quantity) : 0);

            decimal totalCost = 0;
            decimal totalShipping = 0;

            foreach (var o in successOrders)
            {
                if (o.OrderDetails != null)
                {
                    totalCost += o.OrderDetails.Sum(d => d.Quantity * d.ImportPrice);
                }
                if (o.ShippingMethod == "Giao hàng nhanh") totalShipping += 30000;
                else if (o.ShippingMethod == "Giao hàng tiết kiệm") totalShipping += 15000;
            }

            decimal grossProfit = totalRevenue - totalCost - totalShipping;

            // BƯỚC 3: VẼ BIỂU ĐỒ (Giữ nguyên logic của hàm Index)
            var validOrdersForChart = allOrders.Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt").ToList();
            var chartLabels = new List<string>();
            var chartDataList = new List<double>();

            if (isAllTime)
            {
                DateTime sixMonthsAgo = DateTime.Now.AddMonths(-5);
                DateTime earliestDate = validOrdersForChart.Any() ? validOrdersForChart.Min(o => o.OrderDate) : sixMonthsAgo;
                DateTime startDate = earliestDate < sixMonthsAgo ? earliestDate : sixMonthsAgo;

                DateTime tempDate = new DateTime(startDate.Year, startDate.Month, 1);
                DateTime endDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

                while (tempDate <= endDate)
                {
                    chartLabels.Add(tempDate.ToString("MM/yyyy"));
                    var monthTotal = validOrdersForChart.Where(o => o.OrderDate.Year == tempDate.Year && o.OrderDate.Month == tempDate.Month).Sum(o => (double)o.TotalAmount);
                    chartDataList.Add(monthTotal);
                    tempDate = tempDate.AddMonths(1);
                }
            }
            else
            {
                for (DateTime date = dtFrom.Date; date <= dtTo.Date; date = date.AddDays(1))
                {
                    chartLabels.Add(date.ToString("dd/MM"));
                    var dayTotal = validOrdersForChart.Where(o => o.OrderDate.Date == date).Sum(o => (double)o.TotalAmount);
                    chartDataList.Add(dayTotal);
                }
            }

            return Json(new
            {
                totalRevenue = totalRevenue.ToString("N0"),
                totalProfit = grossProfit.ToString("N0"),
                orderCount = allOrders.Count,
                chartLabels = chartLabels,
                chartData = chartDataList
            });
        }
    }
}