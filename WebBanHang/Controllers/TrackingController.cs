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
            if (Session["CustomerID"] == null)
                return Json(new { success = true });

            try
            {
                int customerId = (int)Session["CustomerID"];
                string actType = actionType.ToUpper();

                int weight = 1;
                switch (actType)
                {
                    case "VIEW": weight = 1; break;
                    case "ADD_CART": weight = 5; break;
                    case "BUY": weight = 10; break;
                }

                var existingLog = db.UserBehaviorLogs
                    .FirstOrDefault(l => l.ProductID == productId
                                      && l.CustomerID == customerId
                                      && l.ActionType == actType);

                if (existingLog != null)
                {
                    existingLog.ActionWeight += weight;
                    existingLog.CreatedAt = DateTime.Now;
                }
                else
                {
                    db.UserBehaviorLogs.Add(new UserBehaviorLog
                    {
                        ProductID = productId,
                        ActionType = actType,
                        ActionWeight = weight,
                        CustomerID = customerId, // Lưu theo ID khách hàng
                        SessionID = Session.SessionID,
                        CreatedAt = DateTime.Now
                    });
                }

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
            if (Session["CustomerID"] == null)
                return Json(new { success = true });

            try
            {
                int customerId = (int)Session["CustomerID"];
                var timeLog = db.UserBehaviorLogs
                            .FirstOrDefault(l => l.ProductID == productId
                                              && l.CustomerID == customerId
                                              && l.ActionType == "DWELL_TIME");

                if (timeLog != null)
                {
                    timeLog.ActionWeight += 2;
                    timeLog.CreatedAt = DateTime.Now;
                }
                else
                {
                    db.UserBehaviorLogs.Add(new UserBehaviorLog
                    {
                        SessionID = Session.SessionID,
                        CustomerID = customerId,
                        ProductID = productId,
                        ActionType = "DWELL_TIME",
                        ActionWeight = 2,
                        CreatedAt = DateTime.Now
                    });
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