using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using WebBanHang.Models;

namespace WebBanHang.Controllers
{
    public class BuildPCController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // Hiển thị giao diện chính của bộ chọn cấu hình
        public ActionResult Index()
        {
            return View();
        }

        // API lấy danh sách linh kiện theo loại (ComponentType) qua AJAX
        [HttpGet]
        public JsonResult GetComponents(string type)
        {
            var products = db.Products
                .Where(p => p.ComponentType == type && p.StockQuantity > 0)
                .Select(p => new
                {
                    p.ProductID,
                    p.ProductName,
                    p.ProductPrice,
                    p.ProductImage,
                    p.StockQuantity
                })
                .ToList();

            return Json(products, JsonRequestBehavior.AllowGet);
        }
    }
}