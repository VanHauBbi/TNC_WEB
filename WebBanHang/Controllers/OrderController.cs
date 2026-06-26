using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using System.Threading.Tasks; 
using WebBanHang.Models;
using WebBanHang.Models.ViewModel;
using WebBanHang.Services;

namespace WebBanHang.Controllers
{
    public class OrdersController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();
        private readonly GHNService _ghnService = new GHNService();

        // GET: Orders
        public ActionResult Index()
        {
            var orders = db.Orders.Include(o => o.Customer);
            return View(orders.ToList());
        }

        // GET: Orders/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Order order = db.Orders.Find(id);
            if (order == null)
            {
                return HttpNotFound();
            }
            return View(order);
        }

        // GET: Orders/Create
        public ActionResult Create()
        {
            ViewBag.CustomerID = new SelectList(db.Customers, "CustomerID", "CustomerName");
            return View();
        }

        // GET: Orders/Checkout
        public ActionResult Checkout()
        {
            var cart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;

            if (cart == null || !cart.Items.Any())
            {
                TempData["Message"] = "Giỏ hàng của bạn đang trống!";
                return RedirectToAction("Index", "Cart");
            }

            using (var tempDb = new WebBanHang.Models.MyStoreEntities())
            {
                // BỘ ĐỆM THEO DÕI VOUCHER (Memory Tracker) ĐỂ CHỐNG TRỪ DƯ LƯỢT
                var couponTracker = new Dictionary<int, int>();

                foreach (var item in cart.Items)
                {
                    var product = tempDb.Products.Include("Coupons").SingleOrDefault(p => p.ProductID == item.ProductID);
                    if (product != null)
                    {
                        if (item.OriginalPrice > item.UnitPrice)
                        {
                            var activeCoupon = product.Coupons
                                .Where(c => c.ExpiryDate > DateTime.Now)
                                .OrderByDescending(c => c.DiscountPercentage ?? (c.MaxDiscountAmount ?? 0))
                                .FirstOrDefault();

                            if (activeCoupon != null)
                            {
                                // 1. Nếu mã này chưa có trong sổ nháp, chép số lượt thực tế từ DB vào
                                if (!couponTracker.ContainsKey(activeCoupon.CouponID))
                                {
                                    couponTracker[activeCoupon.CouponID] = activeCoupon.UsageLimit;
                                }

                                // 2. Lấy số lượt còn lại từ Sổ nháp (thay vì DB)
                                int availableLimit = couponTracker[activeCoupon.CouponID];

                                if (availableLimit > 0)
                                {
                                    // 3. Tính số lượng được giảm và Ghi đè lại sổ nháp
                                    int appliedQty = Math.Min(item.Quantity, availableLimit);
                                    item.DiscountableQuantity = appliedQty;

                                    // Trừ đi số lượt vừa nháp để sản phẩm sau không xài lố
                                    couponTracker[activeCoupon.CouponID] -= appliedQty;
                                }
                                else
                                {
                                    item.DiscountableQuantity = 0; // Sổ nháp báo đã hết lượt
                                }
                            }
                            else
                            {
                                item.DiscountableQuantity = 0;
                            }
                        }
                        else
                        {
                            item.DiscountableQuantity = item.Quantity;
                        }
                    }
                }
            }

            var model = new CheckoutVM
            {
                CartItems = cart.Items.ToList(),
                TotalAmount = cart.TotalValue(), // Lúc này TotalValue sẽ tính chính xác dựa trên sổ nháp
                OrderDate = DateTime.Now,
                PaymentStatus = "Chưa thanh toán",
                AvailableCoupons = db.Coupons.Where(c => !c.Products.Any()).OrderByDescending(c => c.CouponID).ToList()
            };

            return View(model);
        }

        // POST: Orders/Checkout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Checkout(CheckoutVM model)
        {
            var cart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;
            if (cart == null || !cart.Items.Any())
            {
                ModelState.AddModelError("", "Giỏ hàng của bạn đang trống!");
                model.AvailableCoupons = db.Coupons.Where(c => !c.Products.Any()).ToList();
                return View(model);
            }

            // 1. CHỐT CHẶN BACKEND: KIỂM TRA TỒN KHO TRƯỚC KHI TẠO ĐƠN
            foreach (var item in cart.Items)
            {
                var checkStock = db.Products.Find(item.ProductID);
                if (checkStock == null || item.Quantity > checkStock.StockQuantity)
                {
                    ModelState.AddModelError("", $"Sản phẩm '{item.ProductName}' chỉ còn {checkStock?.StockQuantity ?? 0} cái trong kho. Vui lòng quay lại giỏ hàng để giảm số lượng.");
                    model.AvailableCoupons = db.Coupons.Where(c => !c.Products.Any()).ToList();
                    return View(model);
                }
            }

            if (!ModelState.IsValid)
            {
                model.AvailableCoupons = db.Coupons.Where(c => !c.Products.Any()).ToList();
                return View(model);
            }

            int customerId = (int)Session["CustomerID"];

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    // Lưu hóa đơn Cha
                    var order = new Order
                    {
                        CustomerID = customerId,
                        OrderDate = DateTime.Now,
                        PaymentStatus = "Chưa thanh toán",
                        ShippingAddress = model.ShippingAddress,
                        ShippingMethod = model.ShippingMethod,
                        PaymentMethod = model.PaymentMethod,
                        TotalAmount = model.TotalAmount
                    };
                    db.Orders.Add(order);
                    db.SaveChanges(); // Lưu để EF sinh ra order.OrderID

                    decimal actualTotalOrderAmount = 0;

                    // 2. TÁCH DÒNG CHI TIẾT, TRỪ VOUCHER ĐA LƯỢNG & [HẠCH TOÁN FIFO]
                    foreach (var item in cart.Items)
                    {
                        var productInDb = db.Products.Include(p => p.Coupons).SingleOrDefault(p => p.ProductID == item.ProductID);
                        if (productInDb != null)
                        {
                            productInDb.StockQuantity -= item.Quantity;
                            if (productInDb.StockQuantity < 0) productInDb.StockQuantity = 0;

                            // =========================================================================
                            // [LÕI KẾ TOÁN FIFO]: TRỪ LÙI KHO LÔ VÀ BÓC TÁCH GIÁ VỐN GIA QUYỀN
                            // =========================================================================
                            int totalQtyNeededForFifo = item.Quantity; // Ví dụ: Mua 5 cái
                            decimal totalFifoCostForThisItem = 0;      // Gom tổng vốn của 5 cái này

                            // Quét các lô hàng cũ nhất còn tồn của sản phẩm này
                            var availableBatches = db.ImportReceiptDetails
                                                     .Where(b => b.ProductID == item.ProductID && b.RemainingQuantity > 0)
                                                     .OrderBy(b => b.DetailID)
                                                     .ToList();

                            foreach (var batch in availableBatches)
                            {
                                if (totalQtyNeededForFifo <= 0) break;

                                if (batch.RemainingQuantity >= totalQtyNeededForFifo)
                                {
                                    // Lô này gánh hết
                                    batch.RemainingQuantity -= totalQtyNeededForFifo;
                                    totalFifoCostForThisItem += (totalQtyNeededForFifo * batch.ImportPrice);
                                    totalQtyNeededForFifo = 0;
                                }
                                else
                                {
                                    // Vét sạch lô này rồi đi tìm lô tiếp theo
                                    int qtyTakenFromThisBatch = batch.RemainingQuantity;
                                    totalFifoCostForThisItem += (qtyTakenFromThisBatch * batch.ImportPrice);
                                    totalQtyNeededForFifo -= qtyTakenFromThisBatch;
                                    batch.RemainingQuantity = 0;
                                }
                                db.Entry(batch).State = EntityState.Modified;
                            }

                            // Cứu cánh: Nếu kho lô bị hụt (do trước đây từng sửa tay), lấy mỏ neo ImportPrice bù vào
                            if (totalQtyNeededForFifo > 0)
                            {
                                decimal fallbackCost = productInDb.ImportPrice;
                                totalFifoCostForThisItem += (totalQtyNeededForFifo * fallbackCost);
                            }

                            // Ra được Giá vốn bình quân 1 sản phẩm của riêng giao dịch này
                            decimal lockedUnitImportCost = item.Quantity > 0 ? (totalFifoCostForThisItem / item.Quantity) : 0;
                            // =========================================================================


                            // --- Xử lý tách dòng Voucher ---
                            int applicableQty = 0;
                            int remainingQty = item.Quantity;

                            if (item.OriginalPrice > item.UnitPrice)
                            {
                                var appliedCoupon = productInDb.Coupons
                                    .Where(c => c.ExpiryDate > DateTime.Now && c.UsageLimit > 0)
                                    .OrderByDescending(c => c.DiscountPercentage ?? (c.MaxDiscountAmount ?? 0))
                                    .FirstOrDefault();

                                if (appliedCoupon != null)
                                {
                                    applicableQty = Math.Min(item.Quantity, appliedCoupon.UsageLimit);
                                    remainingQty = item.Quantity - applicableQty;
                                    appliedCoupon.UsageLimit -= applicableQty;
                                }
                            }

                            // DÒNG 1: Nhóm sản phẩm được áp giá giảm
                            if (applicableQty > 0)
                            {
                                db.OrderDetails.Add(new OrderDetail
                                {
                                    OrderID = order.OrderID,
                                    ProductID = item.ProductID,
                                    Quantity = applicableQty,
                                    UnitPrice = item.UnitPrice,
                                    // [CHỐT HẠ]: Khóa giá vốn FIFO vào dòng này
                                    ImportPrice = lockedUnitImportCost
                                });
                                actualTotalOrderAmount += (applicableQty * item.UnitPrice);
                            }

                            // DÒNG 2: Nhóm sản phẩm rớt lại giá gốc do vượt giới hạn voucher
                            if (remainingQty > 0)
                            {
                                db.OrderDetails.Add(new OrderDetail
                                {
                                    OrderID = order.OrderID,
                                    ProductID = item.ProductID,
                                    Quantity = remainingQty,
                                    UnitPrice = item.OriginalPrice,
                                    // [CHỐT HẠ]: Khóa giá vốn FIFO vào dòng này
                                    ImportPrice = lockedUnitImportCost
                                });
                                actualTotalOrderAmount += (remainingQty * item.OriginalPrice);
                            }
                        }
                    }

                    // 3. Xử lý Voucher TOÀN ĐƠN
                    if (!string.IsNullOrEmpty(model.AppliedVoucherCode))
                    {
                        var globalCoupon = db.Coupons.SingleOrDefault(c => c.Code == model.AppliedVoucherCode);
                        if (globalCoupon != null && globalCoupon.UsageLimit > 0)
                        {
                            globalCoupon.UsageLimit -= 1;
                            actualTotalOrderAmount -= Convert.ToDecimal(Session["VoucherDiscount"] ?? 0);
                        }
                    }

                    order.TotalAmount = actualTotalOrderAmount;
                    db.SaveChanges();
                    transaction.Commit();

                    bool isCommitted = true;

                    // ==========================================================
                    // NHÁNH 1: THANH TOÁN VNPAY (Tuyệt đối không xóa giỏ hàng ở đây)
                    // ==========================================================
                    if (model.PaymentMethod == "VNPAY")
                    {
                        string vnp_Url = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
                        string vnp_TmnCode = "6NQ3MY7C";
                        string vnp_HashSecret = "HASY7LN7TINZAOCJ1JJZHLBTEQK1JQ4H";
                        string vnp_Returnurl = "https://localhost:44329/Orders/PaymentCallback";

                        WebBanHang.Utilities.VnPayLibrary vnpay = new WebBanHang.Utilities.VnPayLibrary();

                        vnpay.AddRequestData("vnp_Version", "2.1.0");
                        vnpay.AddRequestData("vnp_Command", "pay");
                        vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);

                        long tAmount = Convert.ToInt64(order.TotalAmount * 100);
                        vnpay.AddRequestData("vnp_Amount", tAmount.ToString());

                        vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                        vnpay.AddRequestData("vnp_CurrCode", "VND");
                        vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");

                        vnpay.AddRequestData("vnp_Locale", "vn");
                        vnpay.AddRequestData("vnp_OrderInfo", "ThanhToanDonHang_" + order.OrderID.ToString());
                        vnpay.AddRequestData("vnp_OrderType", "other");
                        vnpay.AddRequestData("vnp_ReturnUrl", vnp_Returnurl);
                        vnpay.AddRequestData("vnp_TxnRef", order.OrderID.ToString());

                        string paymentUrl = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
                        return Redirect(paymentUrl);
                    }

                    // ==========================================================
                    // NHÁNH 2: THANH TOÁN TIỀN MẶT (COD) -> XÓA GIỎ HÀNG NGAY LẬP TỨC
                    // ==========================================================
                    var tempCart = Session["BuyNowTempCart"] as WebBanHang.Models.ViewModel.Cart;
                    if (tempCart != null)
                    {
                        Session["Cart"] = tempCart;
                        Session.Remove("BuyNowTempCart");
                    }
                    else
                    {
                        Session.Remove("Cart");
                        Session.Remove("VoucherDiscount");
                    }

                    return RedirectToAction("OrderSuccess", new { id = order.OrderID });
                }
                catch (Exception ex)
                {
                    if (transaction.UnderlyingTransaction.Connection != null)
                    {
                        transaction.Rollback();
                    }
                    ModelState.AddModelError("", "Lỗi hệ thống: " + ex.Message);
                    model.AvailableCoupons = db.Coupons.Where(c => !c.Products.Any()).ToList();
                    return View(model);
                }
            }
        }

        // GET: Orders/OrderSuccess/5
        public ActionResult OrderSuccess(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // Giả định Order là Entity Model chứa thông tin đơn hàng
            var order = db.Orders.Include("OrderDetails.Product").SingleOrDefault(o => o.OrderID == id);

            if (order == null)
            {
                return HttpNotFound();
            }

            // Trả về View với đối tượng Order đã tìm thấy
            return View(order);
        }
        public ActionResult PaymentCallback()
        {
            if (Request.QueryString.AllKeys.Length > 0)
            {
                string vnp_HashSecret = "HASY7LN7TINZAOCJ1JJZHLBTEQK1JQ4H";

                var vnpayData = Request.QueryString;
                WebBanHang.Utilities.VnPayLibrary vnpay = new WebBanHang.Utilities.VnPayLibrary();

                foreach (string s in vnpayData)
                {
                    if (!string.IsNullOrEmpty(s) && s.StartsWith("vnp_"))
                    {
                        vnpay.AddResponseData(s, vnpayData[s]);
                    }
                }

                int orderId = Convert.ToInt32(vnpay.GetResponseData("vnp_TxnRef"));
                string vnp_ResponseCode = vnpay.GetResponseData("vnp_ResponseCode");
                string vnp_SecureHash = Request.QueryString["vnp_SecureHash"];

                bool checkSignature = vnpay.ValidateSignature(vnp_SecureHash, vnp_HashSecret);
                if (checkSignature)
                {
                    // FIX LỖI 1: Phải dùng Include để gọi kèm OrderDetails ra, nếu không vòng lặp foreach sẽ bị Crash
                    var order = db.Orders.Include(o => o.OrderDetails).SingleOrDefault(o => o.OrderID == orderId);

                    if (order != null)
                    {
                        // ==========================================================
                        // NHÁNH THÀNH CÔNG: KHÁCH ĐÃ TRẢ TIỀN -> XÓA GIỎ
                        // ==========================================================
                        if (vnp_ResponseCode == "00")
                        {
                            order.PaymentStatus = "Đã thanh toán";
                            db.SaveChanges();

                            // --- DỌN DẸP GIỎ HÀNG TẠI ĐÂY ---
                            // ✅ FIX: Luôn xóa giỏ hàng và session khi thanh toán thành công
                            Session.Remove("Cart");
                            Session.Remove("VoucherDiscount");
                            Session.Remove("BuyNowTempCart");

                            TempData["Message"] = "Thanh toán đơn hàng qua cổng VNPay thành công!";
                            return RedirectToAction("OrderSuccess", new { id = orderId });
                        }

                        // ==========================================================
                        // NHÁNH THẤT BẠI: HỦY ĐƠN HÀNG & HOÀN TRẢ TỒN KHO (FIFO)
                        // ==========================================================
                        else
                        {
                            // 1. Cập nhật trạng thái thanh toán và DUYỆT
                            order.PaymentStatus = "Thất bại";
                            order.OrderStatus = "Đã hủy";

                            // 2. HOÀN TRẢ TỒN KHO & FIFO
                            foreach (var detail in order.OrderDetails)
                            {
                                var productInDb = db.Products.Find(detail.ProductID);
                                if (productInDb != null)
                                {
                                    productInDb.StockQuantity += detail.Quantity;
                                    db.Entry(productInDb).State = EntityState.Modified; // Bắt buộc báo EF cần update

                                    var latestBatch = db.ImportReceiptDetails
                                                        .Where(b => b.ProductID == detail.ProductID)
                                                        .OrderByDescending(b => b.DetailID)
                                                        .FirstOrDefault();
                                    if (latestBatch != null)
                                    {
                                        latestBatch.RemainingQuantity += detail.Quantity;
                                        db.Entry(latestBatch).State = EntityState.Modified;
                                    }
                                }
                            }

                            // 3. LƯU THAY ĐỔI VÀO DB
                            db.Entry(order).State = EntityState.Modified; // Báo EF lưu trạng thái đơn
                            db.SaveChanges();

                            // 4. PHỤC HỒI GIỎ HÀNG (Nếu có BuyNowTempCart)
                            var tempCart = Session["BuyNowTempCart"] as WebBanHang.Models.ViewModel.Cart;
                            if (tempCart != null)
                            {
                                Session["Cart"] = tempCart;
                                Session.Remove("BuyNowTempCart");
                            }
                            else
                            {
                                // ✅ FIX: Xóa giỏ hàng hiện tại khi thanh toán thất bại
                                Session.Remove("Cart");
                                Session.Remove("VoucherDiscount");
                            }

                            TempData["Error"] = "Giao dịch thất bại. Đơn hàng đã tự động hủy. Mã lỗi: " + vnp_ResponseCode;
                            return RedirectToAction("Index", "Cart");
                        }
                    }
                }
            }

            TempData["Error"] = "Chữ ký bảo mật không hợp lệ!";
            return RedirectToAction("Index", "Cart");
        }

        // ==========================================================
        // BƯỚC 3.1: API LẤY DỮ LIỆU TỈNH/HUYỆN/XÃ VÀ TÍNH PHÍ GHN
        // ==========================================================

        [HttpGet]
        public async Task<ActionResult> GetProvinces() // Đổi JsonResult thành ActionResult
        {
            try
            {
                var provinces = await _ghnService.GetProvincesAsync();
                // Trả về Content kiểu application/json để tránh bị mã hóa kép
                return Content(provinces.ToString(), "application/json");
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public async Task<ActionResult> GetDistricts(int provinceId)
        {
            try
            {
                var districts = await _ghnService.GetDistrictsAsync(provinceId);
                return Content(districts.ToString(), "application/json");
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public async Task<ActionResult> GetWards(int districtId)
        {
            try
            {
                var wards = await _ghnService.GetWardsAsync(districtId);
                return Content(wards.ToString(), "application/json");
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public async Task<JsonResult> CalculateShippingFee(int districtId, string wardCode, int totalQuantity)
        {
            try
            {
                // Tính khối lượng bưu kiện giả định: 500g * số lượng mặt hàng
                int totalWeightInGrams = totalQuantity * 500;
                if (totalWeightInGrams <= 0) totalWeightInGrams = 1000;

                decimal fee = await _ghnService.CalculateFeeAsync(districtId, wardCode, totalWeightInGrams);

                return Json(new { success = true, fee = fee });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // GET: Orders/Tracking/5
        public ActionResult Tracking(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var order = db.Orders.Include(o => o.OrderDetails.Select(od => od.Product)).SingleOrDefault(o => o.OrderID == id);
            if (order == null) return HttpNotFound();

            // Nếu lúc thanh toán bạn lưu là rỗng, thì gán mặc định là Chờ duyệt để code không bị lỗi
            if (string.IsNullOrEmpty(order.OrderStatus))
            {
                order.OrderStatus = "Chờ duyệt";
            }

            return View(order);
        }
    }
}
