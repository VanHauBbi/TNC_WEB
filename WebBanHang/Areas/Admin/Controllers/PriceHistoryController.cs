using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Models.ViewModel;

namespace WebBanHang.Areas.Admin.Controllers
{
    public class PriceHistoryController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities(); // DbContext kết nối database [cite: 15, 152, 381, 507, 560, 676]

        // GET: Admin/PriceHistory
        public ActionResult Index(DateTime? fromDate, DateTime? toDate, int? categoryID)
        {
            ViewBag.Categories = db.Categories.ToList();

            ViewBag.CurrentFromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentCategoryID = categoryID;

            string sql = @"
        SELECT 
            h.PriceHistoryID,
            h.ProductID,
            p.ProductName,
            h.OldPrice,
            h.NewPrice,
            h.ChangeDate
        FROM ProductPriceHistory h
        INNER JOIN Product p ON h.ProductID = p.ProductID
        INNER JOIN Category c ON p.CategoryID = c.CategoryID
        WHERE 1=1";

            List<object> sqlParams = new List<object>();
            int paramIndex = 0;

            if (fromDate.HasValue)
            {
                sql += $" AND h.ChangeDate >= @p{paramIndex}";
                sqlParams.Add(fromDate.Value.Date); // Lấy phần Ngày, bỏ phần Giờ
                paramIndex++;
            }

            if (toDate.HasValue)
            {
                sql += $" AND h.ChangeDate <= @p{paramIndex}";
                sqlParams.Add(toDate.Value.Date.AddDays(1).AddTicks(-1));
                paramIndex++;
            }

            if (categoryID.HasValue)
            {
                sql += $" AND p.CategoryID = @p{paramIndex}";
                sqlParams.Add(categoryID.Value);
                paramIndex++;
            }

            sql += " ORDER BY h.ChangeDate DESC";

            var filteredHistory = db.Database.SqlQuery<PriceHistoryViewModel>(sql, sqlParams.ToArray()).ToList();

            ViewBag.Title = "Lịch sử điều chỉnh hệ thống";
            return View(filteredHistory);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}