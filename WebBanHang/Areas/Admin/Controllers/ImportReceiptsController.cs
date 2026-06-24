using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using WebBanHang.Models; // Sửa lại đúng namespace Models của bạn

namespace WebBanHang.Areas.Admin.Controllers
{
    // 1. DTO hứng dữ liệu mảng JSON từ giao diện gửi lên
    public class ImportReceiptDTO
    {
        public int? POID { get; set; }
        public string ReceivedBy { get; set; }
        public string Note { get; set; }
        public List<ImportDetailDTO> Details { get; set; }
    }

    public class ImportDetailDTO
    {
        public int ProductID { get; set; }
        public int Quantity { get; set; }
        public decimal ImportPrice { get; set; }
    }

    public class ImportReceiptsController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities(); // DB Context của bạn

        // =====================================================================
        // 1. DANH SÁCH PHIẾU NHẬP
        // =====================================================================
        public ActionResult Index()
        {
            var receipts = db.ImportReceipts.OrderByDescending(r => r.ReceivedDate).ToList();
            return View(receipts);
        }

        // =====================================================================
        // 2. MỞ FORM TẠO PHIẾU NHẬP
        // =====================================================================
        public ActionResult Create()
        {
            ViewBag.ProductsList = db.Products.ToList();

            return View();
        }

        // =====================================================================
        // 3. API CHỐT PHIẾU NHẬP KHO (XỬ LÝ ACID TRANSACTION)
        // =====================================================================
        [HttpPost]
        public JsonResult SubmitReceipt(ImportReceiptDTO model)
        {
            if (model == null || model.Details == null || !model.Details.Any())
            {
                return Json(new { success = false, message = "Phiếu nhập không có mặt hàng nào!" });
            }

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    // A. Tạo phần "Cha" (ImportReceipt)
                    var receipt = new ImportReceipt
                    {
                        POID = model.POID,
                        ReceivedDate = DateTime.Now,
                        ReceivedBy = string.IsNullOrEmpty(model.ReceivedBy) ? "Admin Core" : model.ReceivedBy,
                        Note = model.Note,
                        TotalAmount = 0
                    };

                    db.ImportReceipts.Add(receipt);
                    db.SaveChanges(); // Lưu để EF sinh ra receipt.ReceiptID

                    decimal grandTotal = 0;
                    List<string> marginAlerts = new List<string>();

                    // B. Lặp và xử lý các phần "Con" (ImportReceiptDetail)
                    foreach (var item in model.Details)
                    {
                        if (item.Quantity <= 0 || item.ImportPrice < 0) continue;

                        var detail = new ImportReceiptDetail
                        {
                            ReceiptID = receipt.ReceiptID,
                            ProductID = item.ProductID,
                            ImportPrice = item.ImportPrice,
                            ImportQuantity = item.Quantity,
                            // [BIẾN SỐ SỐNG CÒN CỦA FIFO]: Khởi tạo số tồn của lô = đúng số lượng nhập
                            RemainingQuantity = item.Quantity
                        };
                        db.ImportReceiptDetails.Add(detail);

                        grandTotal += (item.ImportPrice * item.Quantity);

                        // --- THỰC THI CÁC CHỐT CHẶN ERP TRÊN BẢNG PRODUCT ---
                        var targetProduct = db.Products.Find(item.ProductID);
                        if (targetProduct != null)
                        {
                            // Chức năng 1: Cộng dồn Tồn kho vật lý
                            targetProduct.StockQuantity += item.Quantity;

                            // Chức năng 2: Tự động kéo mỏ neo giá vốn lên nếu lô này nhập đắt hơn
                            if (item.ImportPrice > targetProduct.ImportPrice)
                            {
                                targetProduct.ImportPrice = item.ImportPrice;
                            }

                            // Chức năng 3: Cảnh báo trượt giá (Bán lỗ)
                            if (item.ImportPrice >= targetProduct.ProductPrice)
                            {
                                marginAlerts.Add($"Mặt hàng [{targetProduct.ProductName}] có giá nhập đợt này ({item.ImportPrice:N0}đ) >= Giá bán niêm yết ({targetProduct.ProductPrice:N0}đ). Nguy cơ bán lỗ!");
                            }

                            db.Entry(targetProduct).State = EntityState.Modified;
                        }
                    }

                    // Cập nhật lại tổng tiền cho phiếu
                    receipt.TotalAmount = grandTotal;
                    db.Entry(receipt).State = EntityState.Modified;

                    db.SaveChanges();
                    transaction.Commit();

                    return Json(new
                    {
                        success = true,
                        receiptId = receipt.ReceiptID,
                        alerts = marginAlerts
                    });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return Json(new { success = false, message = "Lỗi nghiêm trọng: " + ex.Message });
                }
            }
        }

        // =====================================================================
        // 4. XEM CHI TIẾT PHIẾU ĐÃ NHẬP
        // =====================================================================
        public ActionResult Details(int id)
        {
            var receipt = db.ImportReceipts
                            .Include(r => r.ImportReceiptDetails.Select(d => d.Product))
                            .FirstOrDefault(r => r.ReceiptID == id);

            if (receipt == null) return HttpNotFound();
            return View(receipt);
        }
    }
}