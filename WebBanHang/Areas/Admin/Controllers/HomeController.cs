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
            var query = db.Orders.AsNoTracking().AsQueryable();
            if (fromDate.HasValue) query = query.Where(o => o.OrderDate >= fromDate.Value);
            if (toDate.HasValue) query = query.Where(o => o.OrderDate <= toDate.Value);

            var successOrders = query.Where(o =>
                (o.PaymentMethod == "VNPay" && o.PaymentStatus == "Đã thanh toán" &&
                    (o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành")) ||
                (o.PaymentMethod != "VNPay" &&
                    (o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn thành")));

            // SQL chỉ trả về các số tổng hợp, không nạp toàn bộ đơn và chi tiết đơn vào RAM.
            var vm = new AdminDashboardVM
            {
                TotalOrders = query.Count(),
                SuccessOrders = successOrders.Count(),
                CancelledOrders = query.Count(o => o.OrderStatus == "Đã hủy" || o.OrderStatus == "Hủy đơn"),
                TotalRevenue = successOrders.SelectMany(o => o.OrderDetails)
                    .Sum(d => (decimal?)(d.UnitPrice * d.Quantity)) ?? 0m
            };

            var totalCOGS = successOrders.SelectMany(o => o.OrderDetails)
                .Sum(d => (decimal?)(d.Quantity * d.ImportPrice)) ?? 0m;
            var totalShippingFee = successOrders.Sum(o => (decimal?)(
                o.ShippingMethod == "Giao hàng nhanh" ? 30000m :
                o.ShippingMethod == "Giao hàng tiết kiệm" ? 15000m : 0m)) ?? 0m;

            vm.TotalShippingFee = totalShippingFee;
            vm.TotalProfit = vm.TotalRevenue - totalCOGS - totalShippingFee;
            return vm;
        }

        public ActionResult Index()
        {
            var dashboardVM = GetDashboardStatistics(null, null);

            dashboardVM.LowStockProducts = db.Products.AsNoTracking().Where(p => p.StockQuantity < 10).OrderBy(p => p.StockQuantity).Take(5).ToList();
            dashboardVM.LowStockProductCount = db.Products.AsNoTracking().Count(p => p.StockQuantity < 10);

            dashboardVM.TopSellingProducts = db.Products.AsNoTracking()
                .Include(p => p.OrderDetails)
                .OrderByDescending(p => p.OrderDetails.Sum(od => (int?)od.Quantity) ?? 0)
                .Take(5).ToList();

            DateTime sixMonthsAgo = DateTime.Now.AddMonths(-5);
            var monthlyRevenue = db.Orders.AsNoTracking()
                .Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" ||
                            o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt")
                .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(o => o.TotalAmount) })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();

            DateTime earliestDate = monthlyRevenue.Any()
                ? new DateTime(monthlyRevenue[0].Year, monthlyRevenue[0].Month, 1)
                : sixMonthsAgo;
            DateTime startDate = earliestDate < sixMonthsAgo ? earliestDate : sixMonthsAgo;

            DateTime tempDate = new DateTime(startDate.Year, startDate.Month, 1);
            DateTime endDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            var chartLabels = new List<string>();
            var chartDataList = new List<decimal>();

            while (tempDate <= endDate)
            {
                chartLabels.Add(tempDate.ToString("MM/yyyy"));
                var monthTotal = monthlyRevenue
                    .Where(x => x.Year == tempDate.Year && x.Month == tempDate.Month)
                    .Select(x => x.Revenue)
                    .FirstOrDefault();

                chartDataList.Add(monthTotal);
                tempDate = tempDate.AddMonths(1);
            }

            dashboardVM.ChartLabels = chartLabels;
            dashboardVM.ChartData = chartDataList;

            dashboardVM.RecentActivities = new List<TimelineItemVM>();

            var recentChats = db.SupportSessions.AsNoTracking().OrderByDescending(s => s.StartTime).Take(5).ToList();
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

            var recentOrders = db.Orders.AsNoTracking().OrderByDescending(o => o.OrderDate).Take(5).ToList();
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
            DateTime dtFrom = DateTime.Now;
            DateTime dtTo = DateTime.Now;
            bool isAllTime = (filterType == "all" || string.IsNullOrEmpty(fromDate) || fromDate == "2000-01-01");

            if (!isAllTime)
            {
                if (!DateTime.TryParse(fromDate, out dtFrom) || !DateTime.TryParse(toDate, out dtTo))
                    return Json(new { success = false, message = "Khoảng ngày không hợp lệ." });

                // Set giờ kết thúc là cuối ngày (23:59:59)
                dtTo = dtTo.Date.AddDays(1).AddSeconds(-1);
            }

            var statistics = GetDashboardStatistics(isAllTime ? (DateTime?)null : dtFrom,
                                                     isAllTime ? (DateTime?)null : dtTo);

            var chartQuery = db.Orders.AsNoTracking()
                .Where(o => o.PaymentStatus == "Đã thanh toán" || o.OrderStatus == "Đã giao" ||
                            o.OrderStatus == "Hoàn thành" || o.OrderStatus == "Đã duyệt");

            if (!isAllTime)
                chartQuery = chartQuery.Where(o => o.OrderDate >= dtFrom && o.OrderDate <= dtTo);

            var chartLabels = new List<string>();
            var chartDataList = new List<decimal>();

            if (isAllTime)
            {
                // 1. NẾU LỌC TOÀN BỘ (HOẶC MẶC ĐỊNH): Gom nhóm theo Tháng
                DateTime sixMonthsAgo = DateTime.Now.AddMonths(-5);
                var monthlyData = chartQuery
                    .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(o => o.TotalAmount) })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month)
                    .ToList();

                DateTime earliestDate = monthlyData.Any() ? new DateTime(monthlyData[0].Year, monthlyData[0].Month, 1) : sixMonthsAgo;
                DateTime startDate = earliestDate < sixMonthsAgo ? earliestDate : sixMonthsAgo;

                DateTime tempDate = new DateTime(startDate.Year, startDate.Month, 1);
                DateTime endDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

                while (tempDate <= endDate)
                {
                    chartLabels.Add(tempDate.ToString("MM/yyyy"));
                    var monthTotal = monthlyData.Where(x => x.Year == tempDate.Year && x.Month == tempDate.Month).Select(x => x.Revenue).FirstOrDefault();
                    chartDataList.Add(monthTotal);
                    tempDate = tempDate.AddMonths(1);
                }
            }
            else if (dtFrom.Date == dtTo.Date)
            {
                // ====================================================================
                // 2. NẾU CHỈ LỌC TRONG 1 NGÀY (VD: "Hôm nay"): Chia làm 6 mốc x 4 tiếng
                // ====================================================================
                chartLabels = new List<string> { "0h-4h", "4h-8h", "8h-12h", "12h-16h", "16h-20h", "20h-24h" };

                // Khởi tạo 6 cột với giá trị 0
                for (int i = 0; i < 6; i++) chartDataList.Add(0m);

                // Lấy đơn hàng của ngày đó đưa vào RAM (an toàn vì 1 ngày ít dữ liệu)
                var dayOrders = chartQuery.Select(o => new { o.OrderDate, o.TotalAmount }).ToList();

                // Phân bổ doanh thu vào đúng khung giờ
                foreach (var order in dayOrders)
                {
                    int hour = order.OrderDate.Hour;
                    int slot = hour / 4; // Ví dụ: 15h / 4 = 3 (Tương ứng mốc 12h-16h)

                    if (slot >= 0 && slot < 6)
                    {
                        chartDataList[slot] += order.TotalAmount;
                    }
                }
            }
            else
            {
                // 3. NẾU LỌC NHIỀU NGÀY (VD: "Tuần", "Tháng"): Gom nhóm theo từng Ngày
                var dailyData = chartQuery
                    .GroupBy(o => DbFunctions.TruncateTime(o.OrderDate))
                    .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.TotalAmount) })
                    .ToList();

                for (DateTime date = dtFrom.Date; date <= dtTo.Date; date = date.AddDays(1))
                {
                    chartLabels.Add(date.ToString("dd/MM"));
                    var dayTotal = dailyData.Where(x => x.Date == date).Select(x => x.Revenue).FirstOrDefault();
                    chartDataList.Add(dayTotal);
                }
            }

            return Json(new
            {
                success = true,
                totalRevenue = statistics.TotalRevenue.ToString("N0"),
                totalProfit = statistics.TotalProfit.ToString("N0"),
                orderCount = statistics.TotalOrders,
                chartLabels = chartLabels,
                chartData = chartDataList
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
