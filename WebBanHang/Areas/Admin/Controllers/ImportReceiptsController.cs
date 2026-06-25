using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;
using WebBanHang.Models;
using WebBanHang.Models.ViewModel;

namespace WebBanHang.Areas.Admin.Controllers
{
    // =====================================================================
    // DTO HỨNG DỮ LIỆU TỪ GIAO DIỆN
    // =====================================================================
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
        private MyStoreEntities db = new MyStoreEntities();

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
            // Truyền danh sách sản phẩm ra View để chọn
            ViewBag.ProductsList = db.Products.ToList();
            return View();
        }

        // =====================================================================
        // 3. API LẤY GIÁ TỪ NHÀ CUNG CẤP (GITHUB MOCK API)
        // =====================================================================
        [HttpGet]
        public async Task<ActionResult> FetchSupplierPrice(string sku)
        {
            if (string.IsNullOrEmpty(sku))
            {
                return Json(new { success = false, message = "Vui lòng nhập mã SKU" }, JsonRequestBehavior.AllowGet);
            }

            // Ép hệ thống dùng chuẩn bảo mật TLS 1.2
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            using (var client = new HttpClient())
            {
                // Nhớ đổi 'VanHauBbi' thành tên Github của bạn nếu cần
                string apiUrl = $"https://my-json-server.typicode.com/VanHauBbi/TNC_WEB/Products?SKU={sku}";

                try
                {
                    var response = await client.GetAsync(apiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();

                        var productList = JsonConvert.DeserializeObject<List<SupplierProductVM>>(jsonString);
                        var productInfo = productList?.FirstOrDefault();

                        if (productInfo != null)
                        {
                            return Json(new
                            {
                                success = true,
                                price = productInfo.UnitPrice,
                                name = productInfo.ProductName
                            }, JsonRequestBehavior.AllowGet);
                        }
                    }
                    return Json(new { success = false, message = "Không tìm thấy báo giá cho mã SKU này trên hệ thống nhà cung cấp." }, JsonRequestBehavior.AllowGet);
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Lỗi kết nối API: " + ex.Message }, JsonRequestBehavior.AllowGet);
                }
            }
        }

        // =====================================================================
        // 4. API CHỐT PHIẾU NHẬP KHO (XỬ LÝ ACID TRANSACTION & FIFO)
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
                    // A. Tạo phiếu nhập (ImportReceipt)
                    var receipt = new ImportReceipt
                    {
                        POID = model.POID,
                        ReceivedDate = DateTime.Now,
                        ReceivedBy = string.IsNullOrEmpty(model.ReceivedBy) ? "Admin" : model.ReceivedBy,
                        Note = model.Note,
                        TotalAmount = 0
                    };

                    db.ImportReceipts.Add(receipt);
                    db.SaveChanges(); // Lưu để EF sinh ra receipt.ReceiptID

                    decimal grandTotal = 0;
                    List<string> marginAlerts = new List<string>();

                    // B. Xử lý chi tiết (ImportReceiptDetail)
                    foreach (var item in model.Details)
                    {
                        if (item.Quantity <= 0 || item.ImportPrice < 0) continue;

                        var detail = new ImportReceiptDetail
                        {
                            ReceiptID = receipt.ReceiptID,
                            ProductID = item.ProductID,
                            ImportPrice = item.ImportPrice,
                            ImportQuantity = item.Quantity,
                            RemainingQuantity = item.Quantity // Tồn kho của lô này phục vụ FIFO
                        };
                        db.ImportReceiptDetails.Add(detail);

                        grandTotal += (item.ImportPrice * item.Quantity);

                        // Cập nhật tồn kho tổng và cảnh báo giá
                        var targetProduct = db.Products.Find(item.ProductID);
                        if (targetProduct != null)
                        {
                            targetProduct.StockQuantity += item.Quantity;

                            if (item.ImportPrice > targetProduct.ImportPrice)
                            {
                                targetProduct.ImportPrice = item.ImportPrice; // Neo giá vốn cao nhất
                            }

                            if (item.ImportPrice >= targetProduct.ProductPrice)
                            {
                                marginAlerts.Add($"Nguy cơ bán lỗ: [{targetProduct.ProductName}] có giá nhập ({item.ImportPrice:N0}đ) >= Giá bán ra.");
                            }

                            db.Entry(targetProduct).State = EntityState.Modified;
                        }
                    }

                    // Cập nhật tổng tiền cho phiếu
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
        // 5. XEM CHI TIẾT PHIẾU ĐÃ NHẬP
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