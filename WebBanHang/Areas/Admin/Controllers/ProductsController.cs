using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using PagedList;
using PagedList.Mvc;
using WebBanHang.Models.ViewModel;
using WebBanHang.Models;

namespace WebBanHang.Areas.Admin.Controllers
{
    public class ProductsController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // =====================================================================
        // THUẬT TOÁN HỖ TRỢ SINH MÃ SKU
        // =====================================================================

        // Hàm loại bỏ dấu tiếng Việt
        private string RemoveVietnameseTone(string text)
        {
            string[] vietnameseSigns = new string[]
            {
                "aAeEoOuUiIdDyY",
                "áàạảãâấầậẩẫăắằặẳẵ",
                "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ",
                "éèẹẻẽêếềệểễ",
                "ÉÈẸẺẼÊẾỀỆỂỄ",
                "óòọỏõôốồộổỗơớờợởỡ",
                "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ",
                "úùụủũưứừựửữ",
                "ÚÙỤỦŨƯỨỪỰỬỮ",
                "íìịỉĩ",
                "ÍÌỊỈĨ",
                "đ",
                "Đ",
                "ýỳỵỷỹ",
                "ÝỲỴỶỸ"
            };
            for (int i = 1; i < vietnameseSigns.Length; i++)
            {
                for (int j = 0; j < vietnameseSigns[i].Length; j++)
                {
                    text = text.Replace(vietnameseSigns[i][j], vietnameseSigns[0][i - 1]);
                }
            }
            return text;
        }

        // Hàm sinh SKU thông minh dựa trên tên sản phẩm
        private string GenerateSmartSKU(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName)) return "SKU-DEFAULT";

            // 1. Bỏ dấu và viết hoa
            string cleanName = RemoveVietnameseTone(productName).ToUpper();

            // 2. Chỉ giữ lại chữ cái, số và khoảng trắng
            cleanName = new string(cleanName.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());

            // 3. Tách từ và lấy tối đa 3 từ đầu tiên
            var words = cleanName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string baseSku = "SKU-" + string.Join("-", words.Take(3));

            // 4. Kiểm tra trùng lặp trong cơ sở dữ liệu
            string finalSku = baseSku;
            int counter = 1;
            while (db.Products.Any(p => p.SKU == finalSku))
            {
                finalSku = $"{baseSku}-{counter}";
                counter++;
            }

            return finalSku;
        }

        // Thêm API nhỏ này để giao diện gọi lên lấy SKU
        [HttpGet]
        public JsonResult GenerateSKUPreview(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
            {
                return Json(new { success = false, message = "Vui lòng nhập tên sản phẩm!" }, JsonRequestBehavior.AllowGet);
            }

            string generatedSku = GenerateSmartSKU(productName);
            return Json(new { success = true, sku = generatedSku }, JsonRequestBehavior.AllowGet);
        }

        // =====================================================================
        // CÁC ACTION CỦA CONTROLLER
        // =====================================================================

        public ActionResult Index(string searchTerm, decimal? MinPrice, decimal? MaxPrice, string SortOrder, int? page)
        {
            var product = db.Products.Include(p => p.Category).AsQueryable();

            // 1. Áp dụng các bộ lọc tìm kiếm
            if (!string.IsNullOrEmpty(searchTerm))
            {
                product = product.Where(p =>
                    p.ProductName.Contains(searchTerm) ||
                    p.ProductDescription.Contains(searchTerm) ||
                    p.Category.CategoryName.Contains(searchTerm));
            }
            if (MinPrice.HasValue)
            {
                product = product.Where(p => p.ProductPrice >= MinPrice.Value);
            }
            if (MaxPrice.HasValue)
            {
                product = product.Where(p => p.ProductPrice <= MaxPrice.Value);
            }

            // 2. Áp dụng Sắp xếp sản phẩm
            switch (SortOrder)
            {
                case "name_asc":
                    product = product.OrderBy(p => p.ProductName);
                    break;
                case "name_desc":
                    product = product.OrderByDescending(p => p.ProductName);
                    break;
                case "price_asc":
                    product = product.OrderBy(p => p.ProductPrice);
                    break;
                case "price_desc":
                    product = product.OrderByDescending(p => p.ProductPrice);
                    break;
                default:
                    product = product.OrderBy(p => p.ProductID);
                    break;
            }

            // 3. Phân trang
            int pageNumber = page ?? 1;
            int pageSize = 10;

            var model = new ProductSearchVM
            {
                searchTerm = searchTerm,
                MinPrice = MinPrice,
                MaxPrice = MaxPrice,
                sortOrder = SortOrder,
                page = page,
                products = product.ToPagedList(pageNumber, pageSize)
            };

            return View(model);
        }

        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Product product = db.Products.Find(id);
            if (product == null)
            {
                return HttpNotFound();
            }
            return View(product);
        }

        public ActionResult Create()
        {
            ViewBag.CategoryID = new SelectList(db.Categories, "CategoryID", "CategoryName");
            return View(new Product());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "ProductID,CategoryID,ProductName,ProductDescription,ProductPrice,ProductImage,ComponentType,SKU")] Product product)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (string.IsNullOrEmpty(product.SKU))
                    {
                        product.SKU = GenerateSmartSKU(product.ProductName);
                    }

                    product.StockQuantity = 0;
                    product.ImportPrice = 0;
                    product.Status = 0; // Khởi tạo ở trạng thái Nháp (chưa bán ra thị trường)

                    if (string.IsNullOrEmpty(product.ProductDescription))
                    {
                        product.ProductDescription = "Chưa có mô tả";
                    }

                    db.Products.Add(product);
                    db.SaveChanges();
                    TempData["SuccessMessage"] = $"Đã tạo sản phẩm '{product.ProductName}'. Trạng thái hiện tại: Bản nháp.";
                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Lỗi hệ thống: " + ex.Message);
                }
            }

            ViewBag.CategoryID = new SelectList(db.Categories, "CategoryID", "CategoryName", product.CategoryID);
            return View(product);
        }

        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Product product = db.Products.Find(id);
            if (product == null)
            {
                return HttpNotFound();
            }
            ViewBag.CategoryID = new SelectList(db.Categories, "CategoryID", "CategoryName", product.CategoryID);
            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "ProductID,CategoryID,ProductName,ProductDescription,ProductPrice,ProductImage,ComponentType,Status")] Product product)
        {
            if (ModelState.IsValid)
            {
                var existingProduct = db.Products.Find(product.ProductID);
                if (existingProduct == null) return HttpNotFound();

                existingProduct.ProductName = product.ProductName;
                existingProduct.CategoryID = product.CategoryID;
                existingProduct.ComponentType = product.ComponentType;
                existingProduct.ProductDescription = product.ProductDescription;
                existingProduct.ProductPrice = product.ProductPrice;
                existingProduct.Status = product.Status; // Cho phép Admin thay đổi trạng thái

                if (!string.IsNullOrEmpty(product.ProductImage))
                {
                    existingProduct.ProductImage = product.ProductImage;
                }

                db.Entry(existingProduct).State = EntityState.Modified;
                db.SaveChanges();
                TempData["SuccessMessage"] = "Cập nhật sản phẩm thành công!";
                return RedirectToAction("Index");
            }

            ViewBag.CategoryID = new SelectList(db.Categories, "CategoryID", "CategoryName", product.CategoryID);
            return View(product);
        }

        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Product product = db.Products.Find(id);
            if (product == null)
            {
                return HttpNotFound();
            }
            return View(product);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            try
            {
                Product product = db.Products.Find(id);

                bool isUsedInOrder = db.OrderDetails.Any(o => o.ProductID == id);
                if (isUsedInOrder)
                {
                    TempData["ErrorMessage"] = "Không thể xóa sản phẩm vì đang có đơn hàng sử dụng sản phẩm này.";
                    return RedirectToAction("Index");
                }

                db.Products.Remove(product);
                db.SaveChanges();
                TempData["SuccessMessage"] = "Xóa sản phẩm thành công.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Có lỗi xảy ra khi xóa sản phẩm: " + ex.Message;
            }

            return RedirectToAction("Index");
        }
    }
}