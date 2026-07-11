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
            // Rút danh sách Category từ DB gửi ra View
            ViewBag.CategoriesList = db.Categories.ToList();
            return View();
        }

        // =====================================================================
        // 3. API LẤY GIÁ TỪ NHÀ CUNG CẤP (ĐỌC TRỰC TIẾP GITHUB RAW)
        // =====================================================================
        [HttpGet]
        public async Task<ActionResult> FetchSupplierPrice(string sku)
        {
            if (string.IsNullOrEmpty(sku))
            {
                return Json(new { success = false, message = "Vui lòng nhập mã SKU" }, JsonRequestBehavior.AllowGet);
            }

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            using (var client = new HttpClient())
            {
                // Gọi thẳng file RAW từ Github (Bỏ qua my-json-server)
                string apiUrl = "https://raw.githubusercontent.com/VanHauBbi/TNC_WEB/master/db.json";

                try
                {
                    // Chống Github cache bằng cách thêm tham số thời gian ngẫu nhiên
                    var response = await client.GetAsync(apiUrl + "?t=" + DateTime.Now.Ticks);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();

                        // Tạo một lớp tạm để hứng cấu trúc root của db.json { "Products": [ ... ] }
                        var rootData = JsonConvert.DeserializeAnonymousType(jsonString, new { Products = new List<SupplierProductVM>() });

                        if (rootData != null && rootData.Products != null)
                        {
                            // Dùng LINQ để tự tìm SKU trong danh sách tải về
                            var productInfo = rootData.Products.FirstOrDefault(p => p.SKU == sku);

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
                    // Thay thế vòng lặp foreach trong Action SubmitReceipt bằng mã nguồn sau:

                    foreach (var item in model.Details)
                    {
                        if (item.Quantity <= 0 || item.ImportPrice < 0) continue;

                        var detail = new ImportReceiptDetail
                        {
                            ReceiptID = receipt.ReceiptID,
                            ProductID = item.ProductID,
                            ImportPrice = item.ImportPrice,
                            ImportQuantity = item.Quantity,
                            RemainingQuantity = item.Quantity
                        };
                        db.ImportReceiptDetails.Add(detail);

                        grandTotal += (item.ImportPrice * item.Quantity);

                        var targetProduct = db.Products.Find(item.ProductID);
                        if (targetProduct != null)
                        {
                            // [LOGIC MỚI] Kiểm tra xem đây là Hàng Mới Tinh hay Hàng Đang Kinh Doanh
                            bool isFirstTimeIntake = (targetProduct.Status == 0 || targetProduct.ProductPrice == 0);

                            if (isFirstTimeIntake)
                            {
                                // TRƯỜNG HỢP 1: Hàng mới nhập lần đầu -> KHÔNG CHẶN, CHỈ ĐẨY THÔNG BÁO NHẮC
                                targetProduct.Status = 1; // Kích hoạt trạng thái sẵn sàng kinh doanh

                                marginAlerts.Add($"🔔 [HÀNG MỚI]: '{targetProduct.ProductName}' vừa được nhập kho lần đầu (Giá vốn: {item.ImportPrice:N0}đ). Vui lòng sang Quản lý sản phẩm thiết lập Giá Bán!");
                            }
                            else
                            {
                                // TRƯỜNG HỢP 2: Hàng cũ đã có giá -> ÁP DỤNG CHỐT CHẶN CỨNG CHỐNG BÁN LỖ
                                if (item.ImportPrice >= targetProduct.ProductPrice)
                                {
                                    throw new Exception($"CHẶN GIAO DỊCH: Mặt hàng [{targetProduct.ProductName}] có giá nhập đợt mới ({item.ImportPrice:N0}đ) đang CAO HƠN HOẶC BẰNG giá bán niêm yết hiện tại ({targetProduct.ProductPrice:N0}đ). Vui lòng ra nâng giá bán niêm yết trước khi nhập kho!");
                                }
                            }

                            // Cộng dồn tồn kho vật lý và cập nhật giá vốn cao nhất
                            targetProduct.StockQuantity += item.Quantity;
                            if (item.ImportPrice > targetProduct.ImportPrice)
                            {
                                targetProduct.ImportPrice = item.ImportPrice;
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

        // 1. Action lấy danh sách sản phẩm theo Danh mục cho hộp kiểm
        [HttpGet]
        public JsonResult GetProductsByCategory(int categoryId)
        {
            db.Configuration.ProxyCreationEnabled = false; // Ngăn EF tạo vòng lặp tham chiếu
            var products = db.Products
                             .Where(p => p.CategoryID == categoryId)
                             .Select(p => new { p.ProductID, p.ProductName, p.SKU })
                             .ToList();

            return Json(products, JsonRequestBehavior.AllowGet);
        }

        // =====================================================================
        // 2. Action truy xuất báo giá hàng loạt cho danh sách SKU (ĐỌC TRỰC TIẾP RAW)
        // =====================================================================
        [HttpPost]
        public async Task<JsonResult> FetchBatchSupplierPrices(List<string> skuList)
        {
            if (skuList == null || !skuList.Any())
                return Json(new { success = false, message = "Danh sách SKU trống!" });

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            List<SupplierProductVM> matchedProducts = new List<SupplierProductVM>();

            using (var client = new HttpClient())
            {
                // Gọi thẳng file RAW từ Github (Bỏ qua my-json-server)
                string apiUrl = "https://raw.githubusercontent.com/VanHauBbi/TNC_WEB/master/db.json";

                try
                {
                    // Chống Github cache bằng cách thêm tham số thời gian ngẫu nhiên
                    var response = await client.GetAsync(apiUrl + "?t=" + DateTime.Now.Ticks);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();

                        // Tạo một lớp tạm để hứng cấu trúc root của db.json { "Products": [ ... ] }
                        var rootData = JsonConvert.DeserializeAnonymousType(jsonString, new { Products = new List<SupplierProductVM>() });

                        if (rootData != null && rootData.Products != null)
                        {
                            // Đối chiếu các SKU được tick chọn với kho dữ liệu tải về
                            matchedProducts = rootData.Products
                                .Where(sp => skuList.Contains(sp.SKU))
                                .ToList();

                            return Json(new { success = true, data = matchedProducts });
                        }
                    }
                    return Json(new { success = false, message = "Không thể phân tích dữ liệu từ nhà cung cấp." });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Lỗi kết nối API: " + ex.Message });
                }
            }
        }
    }
}