using System;
using System.Linq;
using System.Web.Mvc;
using WebBanHang.Models;

namespace WebBanHang.Controllers
{
    public class TrackingController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        [HttpPost]
        public JsonResult LogBehavior(int productId, string actionType)
        {
            try
            {
                if (!db.Products.Any(p => p.ProductID == productId))
                    return Json(new { success = false, message = "Sản phẩm không tồn tại." });

                var actType = (actionType ?? string.Empty).Trim().ToUpperInvariant();

                int weight = 1;
                switch (actType)
                {
                    case "VIEW": weight = 1; break;
                    case "CLICK": weight = 2; break;
                    case "DWELL_TIME": weight = 2; break;
                    case "ADD_CART": weight = 5; break;
                    case "REMOVE_CART": weight = 3; break;
                    case "PURCHASE": weight = 10; break;
                    default: return Json(new { success = false, message = "Hành vi không hợp lệ." });
                }

                db.UserBehaviorLogs.Add(new UserBehaviorLog
                {
                    ProductID = productId,
                    ActionType = actType,
                    ActionWeight = weight,
                    CustomerID = Session["CustomerID"] == null ? (int?)null : (int)Session["CustomerID"],
                    SessionID = Session.SessionID,
                    CreatedAt = DateTime.Now
                });

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi Cảm Biến: " + ex.Message);
                return Json(new { success = false });
            }
        }

        [HttpPost]
        public JsonResult LogTimeOnPage(int productId, string sessionId)
        {
            try
            {
                if (!db.Products.Any(p => p.ProductID == productId))
                    return Json(new { success = false });

                var cutoff = DateTime.Now.AddMinutes(-1);
                var recent = db.UserBehaviorLogs.FirstOrDefault(x => x.SessionID == Session.SessionID
                    && x.ProductID == productId && x.ActionType == "DWELL_TIME" && x.CreatedAt >= cutoff);
                if (recent == null)
                {
                    db.UserBehaviorLogs.Add(new UserBehaviorLog
                    {
                        SessionID = Session.SessionID,
                        CustomerID = Session["CustomerID"] == null ? (int?)null : (int)Session["CustomerID"],
                        ProductID = productId,
                        ActionType = "DWELL_TIME",
                        ActionWeight = 2,
                        CreatedAt = DateTime.Now
                    });
                }
                else
                {
                    recent.ActionWeight = Math.Min(6, recent.ActionWeight + 1);
                    recent.CreatedAt = DateTime.Now;
                }

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi Đếm Giờ: " + ex.Message);
                return Json(new { success = false });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
