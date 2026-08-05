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
using System.Configuration;
using WebBanHang.Security;

namespace WebBanHang.Controllers
{
    [CustomerSessionAuthorize]
    public class OrdersController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();
        private readonly GHNService _ghnService = new GHNService();

        private List<Coupon> GetAvailablePublicCoupons()
        {
            try { return new MarketingSellingService(db).GetPublicCoupons(); }
            catch { return db.Coupons.Where(c => !c.Products.Any()).OrderByDescending(c => c.CouponID).ToList(); }
        }

        private void PopulateMarketingCheckout(CheckoutVM model, WebBanHang.Models.ViewModel.Cart cart)
        {
            model.AvailableCoupons = GetAvailablePublicCoupons();
            model.AvailablePersonalVouchers = new List<PersonalVoucherVM>();
            model.AutomaticCombo = null;
            if (cart == null || !cart.Items.Any()) return;

            try
            {
                var customerId = (int)Session["CustomerID"];
                var marketing = new MarketingSellingService(db);
                var productIds = cart.Items.Select(x => x.ProductID).ToList();
                model.AvailablePersonalVouchers = marketing.GetCustomerVouchers(customerId, true)
                    .Where(v => v.TargetProductID.HasValue && productIds.Contains(v.TargetProductID.Value)).ToList();
                var combo = marketing.GetBestCombo(cart.Items);
                model.AutomaticCombo = combo.IsValid ? combo : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Không tải được ưu đãi checkout: " + ex.Message);
            }
        }

        private const string CheckoutModeFullCart = "FULL";
        private const string CheckoutModeSelectedItems = "SELECTED";
        private const string CheckoutModeBuyNow = "BUY_NOW";

        private string GetCheckoutMode(bool isBuyNow)
        {
            if (isBuyNow) return CheckoutModeBuyNow;
            return Session["BuyNowTempCart"] is WebBanHang.Models.ViewModel.Cart
                ? CheckoutModeSelectedItems
                : CheckoutModeFullCart;
        }

        private static string GetCheckoutModeFromVnPay(string orderInfo)
        {
            if (!string.IsNullOrWhiteSpace(orderInfo))
            {
                if (orderInfo.EndsWith("_" + CheckoutModeBuyNow, StringComparison.OrdinalIgnoreCase))
                    return CheckoutModeBuyNow;
                if (orderInfo.EndsWith("_" + CheckoutModeSelectedItems, StringComparison.OrdinalIgnoreCase))
                    return CheckoutModeSelectedItems;
            }
            return CheckoutModeFullCart;
        }

        private static void UpdateDatabaseCartAfterPurchase(
            MyStoreEntities context,
            int customerId,
            string checkoutMode,
            IEnumerable<int> purchasedProductIds)
        {
            if (checkoutMode == CheckoutModeBuyNow) return;

            var dbCart = context.Carts.FirstOrDefault(c => c.CustomerID == customerId);
            if (dbCart == null) return;

            var cartItems = context.CartItems.Where(ci => ci.CartID == dbCart.CartID);
            if (checkoutMode == CheckoutModeSelectedItems)
            {
                var purchasedIds = purchasedProductIds.Distinct().ToList();
                cartItems = cartItems.Where(ci => purchasedIds.Contains(ci.ProductID));
            }

            var itemsToRemove = cartItems.ToList();
            if (itemsToRemove.Any()) context.CartItems.RemoveRange(itemsToRemove);
        }

        private void UpdateSessionCartAfterPurchase(string checkoutMode, IEnumerable<int> purchasedProductIds)
        {
            if (checkoutMode == CheckoutModeBuyNow)
            {
                Session.Remove("BuyNowCart");
                return;
            }

            if (checkoutMode == CheckoutModeSelectedItems)
            {
                var originalCart = Session["BuyNowTempCart"] as WebBanHang.Models.ViewModel.Cart;
                if (originalCart != null)
                {
                    foreach (var productId in purchasedProductIds.Distinct())
                        originalCart.RemoveItem(productId);

                    if (originalCart.Items.Any()) Session["Cart"] = originalCart;
                    else Session.Remove("Cart");
                }
                else
                {
                    Session.Remove("Cart");
                }

                Session.Remove("BuyNowTempCart");
                return;
            }

            Session.Remove("Cart");
            Session.Remove("BuyNowTempCart");
        }

        // GET: Orders
        public ActionResult Index()
        {
            var customerId = (int)Session["CustomerID"];
            var orders = db.Orders.Include(o => o.Customer).Where(o => o.CustomerID == customerId);
            return View(orders.ToList());
        }

        // GET: Orders/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            var customerId = (int)Session["CustomerID"];
            Order order = db.Orders.SingleOrDefault(o => o.OrderID == id && o.CustomerID == customerId);
            if (order == null)
            {
                return HttpNotFound();
            }
            return View(order);
        }

        // GET: Orders/Create
        public ActionResult Create()
        {
            return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
        }

        [HttpPost]
        public ActionResult ValidateCartStock()
        {
            var cart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;
            if (cart == null || !cart.Items.Any())
            {
                return Json(new { success = false, message = "Giỏ hàng của bạn đang trống!" });
            }

            foreach (var item in cart.Items)
            {
                var checkStock = db.Products.Find(item.ProductID);
                if (checkStock == null || item.Quantity > checkStock.StockQuantity)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Hệ thống từ chối: Sản phẩm '{item.ProductName}' hiện tại đang hết hàng. Vui lòng kiểm tra hoặc thay đổi sản phẩm!"
                    });
                }
            }

            return Json(new { success = true });
        }

        // GET: Orders/Checkout
        public ActionResult Checkout(bool isBuyNow = false)
        {
            // 1. LẤY ĐÚNG GIỎ HÀNG DỰA VÀO CỜ MUA NGAY
            var cart = isBuyNow ? Session["BuyNowCart"] as WebBanHang.Models.ViewModel.Cart
                                : Session["Cart"] as WebBanHang.Models.ViewModel.Cart;

            if (cart == null || !cart.Items.Any())
            {
                TempData["Message"] = "Giỏ hàng của bạn đang trống!";
                return RedirectToAction("Index", "Cart");
            }

            foreach (var item in cart.Items)
            {
                var checkStock = db.Products.Find(item.ProductID);
                if (checkStock == null || item.Quantity > checkStock.StockQuantity)
                {
                    if (isBuyNow) Session.Remove("BuyNowCart");

                    TempData["Error"] = $"Sản phẩm '{item.ProductName}' hiện chỉ còn {checkStock?.StockQuantity ?? 0} cái. Vui lòng cập nhật lại giỏ hàng!";
                    return RedirectToAction("Index", "Cart");
                }
            }

            // 2. Bắn cờ này ra View để nhét vào thẻ input ẩn
            ViewBag.IsBuyNow = isBuyNow;

            using (var tempDb = new WebBanHang.Models.MyStoreEntities())
            {
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
                                if (!couponTracker.ContainsKey(activeCoupon.CouponID))
                                {
                                    couponTracker[activeCoupon.CouponID] = activeCoupon.UsageLimit;
                                }

                                int availableLimit = couponTracker[activeCoupon.CouponID];

                                if (availableLimit > 0)
                                {
                                    int appliedQty = Math.Min(item.Quantity, availableLimit);
                                    item.DiscountableQuantity = appliedQty;
                                    couponTracker[activeCoupon.CouponID] -= appliedQty;
                                }
                                else
                                {
                                    item.DiscountableQuantity = 0;
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
                TotalAmount = cart.TotalValue(),
                OrderDate = DateTime.Now,
                PaymentStatus = "Chưa thanh toán"
            };

            PopulateMarketingCheckout(model, cart);

            return View(model);
        }

        // POST: Orders/Checkout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Checkout(CheckoutVM model, bool isBuyNow = false)
        {
            ViewBag.IsBuyNow = isBuyNow;
            var cart = isBuyNow ? Session["BuyNowCart"] as WebBanHang.Models.ViewModel.Cart
                        : Session["Cart"] as WebBanHang.Models.ViewModel.Cart;

            if (cart == null || !cart.Items.Any())
            {
                ModelState.AddModelError("", "Giỏ hàng của bạn đang trống!");
                PopulateMarketingCheckout(model, cart);
                model.CartItems = new List<WebBanHang.Models.ViewModel.CartItem>();
                model.TotalAmount = 0;
                return View(model);
            }

            // 1. CHỐT CHẶN BACKEND: KIỂM TRA TỒN KHO TRƯỚC KHI TẠO ĐƠN
            foreach (var item in cart.Items)
            {
                var checkStock = db.Products.Find(item.ProductID);
                if (checkStock == null || item.Quantity > checkStock.StockQuantity)
                {
                    ModelState.AddModelError("", $"Sản phẩm '{item.ProductName}' chỉ còn {checkStock?.StockQuantity ?? 0} cái trong kho.");
                    PopulateMarketingCheckout(model, cart);

                    // --- BẮT BUỘC PHẢI THÊM 2 DÒNG NÀY CHỖ NÀY ---
                    model.CartItems = cart.Items.ToList();
                    model.TotalAmount = cart.TotalValue();

                    return View(model);
                }
            }

            if (!ModelState.IsValid)
            {
                PopulateMarketingCheckout(model, cart);

                model.CartItems = cart.Items.ToList();
                model.TotalAmount = cart.TotalValue();

                return View(model);
            }

            int customerId = (int)Session["CustomerID"];
            bool isVnPay = string.Equals(model.PaymentMethod, "VNPAY", StringComparison.OrdinalIgnoreCase);
            string checkoutMode = GetCheckoutMode(isBuyNow);
            var purchasedProductIds = cart.Items.Select(x => x.ProductID).Distinct().ToList();

            string vnpUrl = null;
            string vnpTmnCode = null;
            string vnpHashSecret = null;
            string vnpReturnUrl = null;
            if (isVnPay)
            {
                vnpUrl = ConfigurationManager.AppSettings["VnPayUrl"];
                vnpTmnCode = ConfigurationManager.AppSettings["VnPayTmnCode"];
                vnpHashSecret = ConfigurationManager.AppSettings["VnPayHashSecret"];
                vnpReturnUrl = ConfigurationManager.AppSettings["VnPayReturnUrl"];

                if (string.IsNullOrWhiteSpace(vnpUrl) || string.IsNullOrWhiteSpace(vnpTmnCode)
                    || string.IsNullOrWhiteSpace(vnpHashSecret) || string.IsNullOrWhiteSpace(vnpReturnUrl))
                {
                    ModelState.AddModelError("", "VNPay chưa được cấu hình đầy đủ trong Web.config.");
                    model.CartItems = cart.Items.ToList();
                    model.TotalAmount = cart.TotalValue();
                    PopulateMarketingCheckout(model, cart);
                    return View(model);
                }
            }

            PromotionEvaluation marketingPromotion = null;
            try
            {
                var evaluated = new MarketingSellingService(db)
                    .EvaluateBestPromotion(model.AppliedVoucherCode, customerId, cart.Items);
                if (evaluated.IsValid)
                {
                    marketingPromotion = evaluated;
                }
                else if (!string.IsNullOrWhiteSpace(model.AppliedVoucherCode))
                {
                    ModelState.AddModelError("", evaluated.Message);
                    model.CartItems = cart.Items.ToList();
                    model.TotalAmount = cart.TotalValue();
                    PopulateMarketingCheckout(model, cart);
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Không đánh giá được Marketing Selling: " + ex.Message);
            }

            // MÔ PHỎNG GIÁ VỐN & CẢNH BÁO LỢI NHUẬN (Không hiển thị ra View)
            var simulationService = new WebBanHang.Services.OrderCostSimulationService();
            var simResult = simulationService.SimulateCartCost(cart, db);
            string paymentUrl = null;

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
                        TotalAmount = 0, // Sẽ cập nhật lại ở dưới
                        IsMarginViolated = simResult.IsViolatingMargin
                    };
                    db.Orders.Add(order);
                    db.SaveChanges();

                    // Khởi tạo các bản ghi ngoại lệ (Post-Audit Log)
                    if (simResult.IsViolatingMargin)
                    {
                        foreach (var violation in simResult.Violations)
                        {
                            db.PriceExceptionLogs.Add(new PriceExceptionLog
                            {
                                OrderID = order.OrderID,
                                ProductID = violation.ProductID,
                                TotalRevenue = violation.TotalRevenue,
                                TotalCOGS = violation.TotalCOGS,
                                MarginPercentage = violation.MarginPercentage,
                                CreatedAt = DateTime.Now
                            });
                        }
                        db.SaveChanges();
                    }

                    decimal actualTotalOrderAmount = 0m;

                    // 2. TÁCH DÒNG CHI TIẾT & HẠCH TOÁN FIFO
                    foreach (var item in cart.Items)
                    {
                        var productInDb = db.Products.Include(p => p.Coupons).SingleOrDefault(p => p.ProductID == item.ProductID);
                        if (productInDb == null) continue;

                        productInDb.StockQuantity -= item.Quantity;
                        if (productInDb.StockQuantity < 0) productInDb.StockQuantity = 0;

                        int needed = item.Quantity;
                        decimal totalFifoCost = 0m;

                        var batches = db.ImportReceiptDetails
                                        .Where(b => b.ProductID == item.ProductID && b.RemainingQuantity > 0)
                                        .OrderBy(b => b.DetailID)
                                        .ToList();

                        foreach (var batch in batches)
                        {
                            if (needed <= 0) break;
                            if (batch.RemainingQuantity >= needed)
                            {
                                batch.RemainingQuantity -= needed;
                                totalFifoCost += needed * batch.ImportPrice;
                                needed = 0;
                            }
                            else
                            {
                                int take = batch.RemainingQuantity;
                                totalFifoCost += take * batch.ImportPrice;
                                needed -= take;
                                batch.RemainingQuantity = 0;
                            }
                            db.Entry(batch).State = EntityState.Modified;
                        }

                        if (needed > 0)
                        {
                            totalFifoCost += needed * productInDb.ImportPrice;
                        }

                        decimal lockedUnitImportCost = item.Quantity > 0 ? (totalFifoCost / item.Quantity) : 0m;

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

                        if (applicableQty > 0)
                        {
                            db.OrderDetails.Add(new OrderDetail
                            {
                                OrderID = order.OrderID,
                                ProductID = item.ProductID,
                                Quantity = applicableQty,
                                UnitPrice = item.UnitPrice,
                                ImportPrice = lockedUnitImportCost
                            });
                            actualTotalOrderAmount += applicableQty * item.UnitPrice;
                        }

                        if (remainingQty > 0)
                        {
                            db.OrderDetails.Add(new OrderDetail
                            {
                                OrderID = order.OrderID,
                                ProductID = item.ProductID,
                                Quantity = remainingQty,
                                UnitPrice = item.OriginalPrice,
                                ImportPrice = lockedUnitImportCost
                            });
                            actualTotalOrderAmount += remainingQty * item.OriginalPrice;
                        }
                    }

                    if (marketingPromotion != null && marketingPromotion.IsValid)
                    {
                        actualTotalOrderAmount = Math.Max(0m, actualTotalOrderAmount - marketingPromotion.DiscountAmount);
                        order.DiscountAmount = marketingPromotion.DiscountAmount;
                        if (marketingPromotion.CouponID.HasValue)
                            order.CouponID = marketingPromotion.CouponID.Value;
                        new MarketingSellingService(db).RecordPromotion(order.OrderID, marketingPromotion);
                    }

                    // ✅ FIX LỖI 2: Lấy phí ship từ Server-side (Session) thay vì từ Client gửi lên
                    decimal shippingFee = 0m;
                    if (Session["ShippingFee"] != null)
                    {
                        shippingFee = Convert.ToDecimal(Session["ShippingFee"]);
                    }
                    actualTotalOrderAmount += shippingFee;

                    order.TotalAmount = actualTotalOrderAmount;

                    // COD chỉ xóa các sản phẩm đã mua. Với VNPay, chờ callback thành công mới xóa giỏ.
                    if (!isVnPay)
                        UpdateDatabaseCartAfterPurchase(db, customerId, checkoutMode, purchasedProductIds);

                    db.SaveChanges();

                    // Tạo URL trước khi commit để lỗi cấu hình/chữ ký không tạo ra đơn hàng nửa chừng.
                    if (isVnPay)
                    {
                        var vnpay = new WebBanHang.Utilities.VnPayLibrary();
                        vnpay.AddRequestData("vnp_Version", "2.1.0");
                        vnpay.AddRequestData("vnp_Command", "pay");
                        vnpay.AddRequestData("vnp_TmnCode", vnpTmnCode);
                        vnpay.AddRequestData("vnp_Amount", Convert.ToInt64(actualTotalOrderAmount * 100m).ToString());
                        vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                        vnpay.AddRequestData("vnp_CurrCode", "VND");
                        vnpay.AddRequestData("vnp_IpAddr", Request.UserHostAddress ?? "127.0.0.1");
                        vnpay.AddRequestData("vnp_Locale", "vn");
                        vnpay.AddRequestData("vnp_OrderInfo", "ThanhToanDonHang_" + order.OrderID + "_" + checkoutMode);
                        vnpay.AddRequestData("vnp_OrderType", "other");
                        vnpay.AddRequestData("vnp_ReturnUrl", vnpReturnUrl);
                        vnpay.AddRequestData("vnp_TxnRef", order.OrderID.ToString());
                        paymentUrl = vnpay.CreateRequestUrl(vnpUrl, vnpHashSecret);
                    }

                    transaction.Commit();

                    try
                    {
                        string currentSession = Session.SessionID;

                        if (!isVnPay && cart != null && cart.Items.Any())
                        {
                            foreach (var cartItem in cart.Items)
                            {
                                db.UserBehaviorLogs.Add(new UserBehaviorLog
                                {
                                    ProductID = cartItem.ProductID,
                                    ActionType = "PURCHASE",
                                    ActionWeight = 10,
                                    CustomerID = customerId,
                                    SessionID = currentSession,
                                    CreatedAt = DateTime.Now
                                });
                            }
                            db.SaveChanges();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Lỗi ghi log BUY: " + ex.Message);
                    }

                    // ==========================================================
                    // CHẠY ĐỒNG BỘ: TỰ ĐỘNG HUẤN LUYỆN LẠI AI (HYBRID MODEL)
                    // ==========================================================
                    try
                    {
                        var hybridService = new WebBanHang.Services.SmartRecommendationService();
                        // Chạy với cấu hình: Tin cậy > 20%, Support > 1, Utility > 100k
                        hybridService.RunHybridAlgorithm(0.2, 1, 100000m);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Lỗi AI: " + ex.Message);
                    }

                    // VNPay giữ nguyên giỏ cho đến khi cổng thanh toán callback thành công.
                    if (isVnPay) return Redirect(paymentUrl);

                    UpdateSessionCartAfterPurchase(checkoutMode, purchasedProductIds);

                    Session.Remove("VoucherDiscount");
                    Session.Remove("MarketingPromotion");
                    Session.Remove("ShippingFee");

                    return RedirectToAction("OrderSuccess", new { id = order.OrderID });
                }
                catch (Exception ex)
                {
                    try { transaction.Rollback(); } catch { }

                    System.Diagnostics.Trace.TraceError("Lỗi checkout: " + ex);
                    ModelState.AddModelError("", "Không thể hoàn tất đơn hàng. Vui lòng thử lại hoặc liên hệ hỗ trợ.");
                    PopulateMarketingCheckout(model, cart);

                    if (cart != null)
                    {
                        model.CartItems = cart.Items.ToList();
                        model.TotalAmount = cart.TotalValue();
                    }
                    else
                    {
                        // ✅ Bảo vệ 2 lớp: Chống Crash View ngay cả khi có lỗi hệ thống văng ra lúc Session đã mất
                        model.CartItems = new List<WebBanHang.Models.ViewModel.CartItem>();
                        model.TotalAmount = 0;
                    }

                    return View(model);
                }
            }
        }

        // GET: Orders/OrderSuccess/5
        public ActionResult OrderSuccess(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var customerId = (int)Session["CustomerID"];
            var order = db.Orders.Include("OrderDetails.Product").SingleOrDefault(o => o.OrderID == id && o.CustomerID == customerId);
            if (order == null) return HttpNotFound();

            return View(order);
        }

        [AllowAnonymous]
        public ActionResult PaymentCallback()
        {
            if (Request.QueryString.AllKeys.Length > 0)
            {
                string vnp_HashSecret = ConfigurationManager.AppSettings["VnPayHashSecret"];
                var vnpayData = Request.QueryString;
                WebBanHang.Utilities.VnPayLibrary vnpay = new WebBanHang.Utilities.VnPayLibrary();

                foreach (string s in vnpayData)
                {
                    if (!string.IsNullOrEmpty(s) && s.StartsWith("vnp_"))
                    {
                        vnpay.AddResponseData(s, vnpayData[s]);
                    }
                }

                string vnp_ResponseCode = vnpay.GetResponseData("vnp_ResponseCode");
                string vnp_SecureHash = Request.QueryString["vnp_SecureHash"];

                if (string.IsNullOrWhiteSpace(vnp_HashSecret))
                    return new HttpStatusCodeResult(HttpStatusCode.ServiceUnavailable, "VNPay chưa được cấu hình.");

                bool checkSignature = vnpay.ValidateSignature(vnp_SecureHash, vnp_HashSecret);
                if (checkSignature)
                {
                    int orderId;
                    if (!int.TryParse(vnpay.GetResponseData("vnp_TxnRef"), out orderId))
                        return new HttpStatusCodeResult(HttpStatusCode.BadRequest, "Mã đơn hàng VNPay không hợp lệ.");

                    var order = db.Orders.Include(o => o.OrderDetails).SingleOrDefault(o => o.OrderID == orderId);
                    if (order != null)
                    {
                        long paidAmount;
                        var amountIsValid = long.TryParse(vnpay.GetResponseData("vnp_Amount"), out paidAmount)
                                            && paidAmount == Convert.ToInt64(order.TotalAmount * 100m);
                        if (!amountIsValid)
                            return new HttpStatusCodeResult(HttpStatusCode.BadRequest, "Số tiền VNPay không khớp đơn hàng.");

                        if (vnp_ResponseCode == "00")
                        {
                            bool wasAlreadyPaid = order.PaymentStatus == "Đã thanh toán";
                            string checkoutMode = GetCheckoutModeFromVnPay(vnpay.GetResponseData("vnp_OrderInfo"));
                            var purchasedProductIds = order.OrderDetails.Select(x => x.ProductID).Distinct().ToList();

                            if (!wasAlreadyPaid)
                            {
                                order.PaymentStatus = "Đã thanh toán";
                                db.SaveChanges();

                                // Log hành vi là tác vụ phụ, không được chặn trang thành công của VNPay.
                                try
                                {
                                    foreach (var productId in purchasedProductIds)
                                    {
                                        db.UserBehaviorLogs.Add(new UserBehaviorLog
                                        {
                                            ProductID = productId,
                                            ActionType = "PURCHASE",
                                            ActionWeight = 10,
                                            CustomerID = order.CustomerID,
                                            SessionID = Session.SessionID,
                                            CreatedAt = DateTime.Now
                                        });
                                    }
                                    db.SaveChanges();
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Trace.TraceWarning("Không ghi được PURCHASE sau VNPay: " + ex.Message);
                                }
                            }

                            // ✅ Cập nhật AI khi đơn VNPay thanh toán thành công
                            try { new WebBanHang.Services.SmartRecommendationService().RunHybridAlgorithm(0.2, 1, 100000m); } catch { }

                            // Dọn giỏ DB là tác vụ hậu xử lý; nếu lỗi vẫn phải trả khách về trang thành công.
                            try
                            {
                                using (var cleanupDb = new MyStoreEntities())
                                {
                                    UpdateDatabaseCartAfterPurchase(cleanupDb, order.CustomerID, checkoutMode, purchasedProductIds);
                                    cleanupDb.SaveChanges();
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Trace.TraceWarning("Không dọn được giỏ DB sau VNPay: " + ex.Message);
                            }

                            UpdateSessionCartAfterPurchase(checkoutMode, purchasedProductIds);
                            Session.Remove("VoucherDiscount");
                            Session.Remove("MarketingPromotion");
                            Session.Remove("ShippingFee");

                            TempData["Message"] = "Thanh toán đơn hàng qua cổng VNPay thành công!";
                            return RedirectToAction("OrderSuccess", new { id = orderId });
                        }
                        else
                        {
                            order.PaymentStatus = "Thất bại";
                            order.OrderStatus = "Đã hủy";

                            try { new MarketingSellingService(db).RollbackPromotions(order.OrderID); }
                            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Không hoàn tác được ưu đãi: " + ex.Message); }

                            string currentSession = Session.SessionID;

                            foreach (var detail in order.OrderDetails)
                            {
                                var productInDb = db.Products.Find(detail.ProductID);
                                if (productInDb != null)
                                {
                                    productInDb.StockQuantity += detail.Quantity;
                                    db.Entry(productInDb).State = EntityState.Modified;

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

                                var fakeBuyLog = db.UserBehaviorLogs.FirstOrDefault(l =>
                                    l.ProductID == detail.ProductID &&
                                    l.SessionID == currentSession &&
                                    l.ActionType == "BUY");

                                if (fakeBuyLog != null)
                                {
                                    fakeBuyLog.ActionWeight -= 10;
                                    if (fakeBuyLog.ActionWeight <= 0)
                                    {
                                        db.UserBehaviorLogs.Remove(fakeBuyLog); // Trừ về 0 thì dọn rác luôn
                                    }
                                    else
                                    {
                                        db.Entry(fakeBuyLog).State = EntityState.Modified;
                                    }
                                }
                                // =========================================================
                            }

                            db.Entry(order).State = EntityState.Modified;
                            db.SaveChanges();

                            // =====================================================================
                            // FIX LỖI: KHÔI PHỤC GIỎ HÀNG KHI HỦY VNPAY
                            // =====================================================================
                            // ✅ Cập nhật lại AI để hệ thống KHÔNG học dữ liệu rác từ đơn hàng bị hủy do lỗi thanh toán
                            try { new WebBanHang.Services.SmartRecommendationService().RunHybridAlgorithm(0.2, 1, 100000m); } catch { }

                            var tempCart = Session["BuyNowTempCart"] as WebBanHang.Models.ViewModel.Cart;

                            // Trường hợp 1: Khách dùng nút "Mua Ngay" hoặc "Chọn vài món"
                            if (tempCart != null)
                            {
                                Session["Cart"] = tempCart; // Trả lại giỏ hàng gốc
                                Session.Remove("BuyNowTempCart"); // Xóa Session tạm
                            }
                            // Trường hợp 2: Giỏ hàng bị rớt mất Session, ta gọi lại từ Database
                            else if (Session["Cart"] == null && Session["CustomerID"] != null)
                            {
                                int custId = (int)Session["CustomerID"];
                                var dbCart = db.Carts.Include(c => c.CartItems).SingleOrDefault(c => c.CustomerID == custId);

                                if (dbCart != null && dbCart.CartItems.Any())
                                {
                                    var sessionCart = new WebBanHang.Models.ViewModel.Cart();
                                    foreach (var dbItem in dbCart.CartItems)
                                    {
                                        var product = db.Products.Include(p => p.Category).Include(p => p.Coupons)
                                                        .SingleOrDefault(p => p.ProductID == dbItem.ProductID);
                                        if (product != null)
                                        {
                                            decimal discountPercent;
                                            decimal finalUnitPrice = WebBanHang.Utilities.PriceHelper.GetDiscountedPrice(product, out discountPercent);
                                            if (finalUnitPrice < 0) finalUnitPrice = 0;

                                            sessionCart.AddItem(product.ProductID, product.ProductImage, product.ProductName,
                                                                finalUnitPrice, product.ProductPrice, dbItem.Quantity, product.Category?.CategoryName);
                                        }
                                    }
                                    Session["Cart"] = sessionCart;
                                }
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
        public async Task<ActionResult> GetProvinces()
        {
            try
            {
                var provinces = await _ghnService.GetProvincesAsync();
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
                int totalWeightInGrams = totalQuantity * 500;
                if (totalWeightInGrams <= 0) totalWeightInGrams = 1000;

                decimal fee = await _ghnService.CalculateFeeAsync(districtId, wardCode, totalWeightInGrams);

                // ✅ LƯU PHÍ SHIP VÀO SESSION NGAY TẠI ĐÂY
                Session["ShippingFee"] = fee;

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

            if (string.IsNullOrEmpty(order.OrderStatus))
            {
                order.OrderStatus = "Chờ duyệt";
            }

            return View(order);
        }
    }
}
