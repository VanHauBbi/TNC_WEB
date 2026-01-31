using WebBanHang.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using WebBanHang.Models.ViewModel;
using PagedList;
using System.Drawing;


namespace WebBanHang.Areas.Admin.Controllers
{
    public class HomeController : Controller
    {
        // GET: Admin/Home
        private MyStoreEntities db = new MyStoreEntities(); // DbContext của bạn

        // Trang chủ Admin - Hiển thị danh sách sản phẩm
        public ActionResult Index(int? page, int? pageSize)
        {
            var currentPage = page ?? 1;
            var size = pageSize ?? 20;

            var products = db.Products
                             .OrderBy(p => p.ProductID)
                             .ToPagedList(currentPage, size);

            var model = new HomeProductVM
            {
                Products = products,
                
            };

            return View(model);
        }

        public ActionResult About()
        {
            // Có thể thêm ViewBag để truyền dữ liệu vào View nếu cần
            ViewBag.Message = "Chào mừng bạn đến với trang giới thiệu của chúng tôi.";
            return View();
        }
        public ActionResult Orders()
        {
            return View();
        }

        public ActionResult ProductSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                // Nếu từ khóa trống, có thể chuyển hướng về trang chủ hoặc trang danh mục
                return RedirectToAction("Index");
            }

            // Thực hiện truy vấn (ví dụ tìm kiếm trong tên sản phẩm)
            var searchResults = db.Products
                                  .Where(p => p.ProductName.Contains(query))
                                  .ToList();

            ViewBag.SearchQuery = query; // Lưu từ khóa để hiển thị lại

            // Trả về một View để hiển thị kết quả tìm kiếm (ví dụ: SearchResults.cshtml)
            return View("SearchResults", searchResults);
        }

    }
}