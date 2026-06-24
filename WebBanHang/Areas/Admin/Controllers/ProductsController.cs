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

        public ActionResult Index(string searchTerm, decimal? MinPrice, decimal? MaxPrice, string SortOrder, int? page)
        {
            var product = db.Products.Include(p => p.Category).AsQueryable(); // THÊM INCLUDE CATEGORY

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
                    product = product.OrderBy(p => p.ProductID); // Sắp xếp mặc định theo ID
                    break;
            }

            // 3. Phân trang
            int pageNumber = page ?? 1;
            int pageSize = 10; // Thay đổi từ 5 lên 10 để Admin dễ quản lý hơn

            var model = new ProductSearchVM
            {
                searchTerm = searchTerm,
                MinPrice = MinPrice,
                MaxPrice = MaxPrice,
                sortOrder = SortOrder,
                page = page, // Lưu lại số trang hiện tại
                products = product.ToPagedList(pageNumber, pageSize)
            };

            return View(model);
        }


        // GET: Admin/Products/Details/5
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

        // GET: Admin/Products/Create
        public ActionResult Create()
        {
            ViewBag.CategoryID = new SelectList(db.Categories, "CategoryID", "CategoryName");
            return View(new Product());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "ProductID,CategoryID,ProductName,ProductDescription,ProductPrice,ImportPrice,ProductImage")] Product product)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    product.StockQuantity = 0;

                    db.Products.Add(product);
                    db.SaveChanges();
                    TempData["SuccessMessage"] = $"Đã tạo danh mục '{product.ProductName}'. Vui lòng sang phân hệ Nhập Kho để nhập số lượng!";
                    return RedirectToAction("Index");
                }
                catch (System.Data.Entity.Validation.DbEntityValidationException ex)
                {
                    var errorDetailLists = ex.EntityValidationErrors
                        .SelectMany(validationResult => validationResult.ValidationErrors)
                        .Select(validationError => $"Cột [{validationError.PropertyName}]: {validationError.ErrorMessage}");

                    string fullSqlErrors = string.Join(" | ", errorDetailLists);

                    ModelState.AddModelError("", "LỖI TỪ DATABASE: " + fullSqlErrors);
                }
                catch (Exception genEx)
                {
                    ModelState.AddModelError("", "Lỗi hệ thống khác: " + genEx.Message);
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
        public ActionResult Edit([Bind(Include = "ProductID,CategoryID,ProductName,ProductDescription,ProductPrice,ProductImage")] Product product)
        {
            if (ModelState.IsValid)
            {
                var existingProduct = db.Products.Find(product.ProductID);
                if (existingProduct == null) return HttpNotFound();

                existingProduct.ProductName = product.ProductName;
                existingProduct.CategoryID = product.CategoryID;
                existingProduct.ProductDescription = product.ProductDescription;
                existingProduct.ProductPrice = product.ProductPrice;

                if (!string.IsNullOrEmpty(product.ProductImage))
                {
                    existingProduct.ProductImage = product.ProductImage;
                }

                db.Entry(existingProduct).State = EntityState.Modified;
                db.SaveChanges();
                TempData["SuccessMessage"] = "Cập nhật thông tin sản phẩm thành công!";
                return RedirectToAction("Index");
            }

            ViewBag.CategoryID = new SelectList(db.Categories, "CategoryID", "CategoryName", product.CategoryID);
            return View(product);
        }

        // GET: Admin/Products/Delete/5
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

        // POST: Admin/Products/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            try
            {
                Product product = db.Products.Find(id);

                // Kiểm tra ràng buộc OrderDetail
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
