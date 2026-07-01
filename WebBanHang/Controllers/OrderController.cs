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

            // MÔ PHỎNG GIÁ VỐN & CẢNH BÁO LỢI NHUẬN (Không hiển thị ra View)
            var simulationService = new WebBanHang.Services.OrderCostSimulationService();
            var simResult = simulationService.SimulateCartCost(cart, db);

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

                    // ✅ FIX LỖI 5: Validate Coupon Toàn đơn chặt chẽ (Check ExpiryDate)
                    if (!string.IsNullOrEmpty(model.AppliedVoucherCode))
                    {
                        var globalCoupon = db.Coupons.SingleOrDefault(c => c.Code == model.AppliedVoucherCode);
                        if (globalCoupon != null && globalCoupon.UsageLimit > 0 && globalCoupon.ExpiryDate >= DateTime.Now)
                        {
                            globalCoupon.UsageLimit -= 1;
                            actualTotalOrderAmount -= Convert.ToDecimal(Session["VoucherDiscount"] ?? 0);
                        }
                    }

                    // ✅ FIX LỖI 2: Lấy phí ship từ Server-side (Session) thay vì từ Client gửi lên
                    decimal shippingFee = 0m;
                    if (Session["ShippingFee"] != null)
                    {
                        shippingFee = Convert.ToDecimal(Session["ShippingFee"]);
                    }
                    actualTotalOrderAmount += shippingFee;

                    order.TotalAmount = actualTotalOrderAmount;
                    db.SaveChanges();
                    transaction.Commit();

                    // Xử lý VNPAY
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

                        long tAmount = Convert.ToInt64(actualTotalOrderAmount * 100);
                        vnpay.AddRequestData("vnp_Amount", tAmount.ToString());

                        vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                        vnpay.AddRequestData("vnp_CurrCode", "VND");
                        vnpay.AddRequestData("vnp_IpAddr", Request.UserHostAddress ?? "127.0.0.1");

                        vnpay.AddRequestData("vnp_Locale", "vn");
                        vnpay.AddRequestData("vnp_OrderInfo", "ThanhToanDonHang_" + order.OrderID.ToString());
                        vnpay.AddRequestData("vnp_OrderType", "other");
                        vnpay.AddRequestData("vnp_ReturnUrl", vnp_Returnurl);
                        vnpay.AddRequestData("vnp_TxnRef", order.OrderID.ToString());

                        string paymentUrl = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
                        return Redirect(paymentUrl);
                    }

                    // ✅ FIX LỖI 1: Xử lý Session Giỏ hàng chuẩn cho COD (Không bị null đè)
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
                    // Đã xóa dòng Session["Cart"] = null gây lỗi;

                    // Xóa Session ShippingFee sau khi đặt hàng xong
                    Session.Remove("ShippingFee");

                    return RedirectToAction("OrderSuccess", new { id = order.OrderID });
                }
                catch (Exception ex)
                {
                    // ✅ FIX LỖI 4: Try-catch khi Rollback để tránh sập web nếu Connection chết
                    try { transaction.Rollback(); } catch { /* ignore */ }

                    ModelState.AddModelError("", "Lỗi hệ thống: " + ex.Message);
                    model.AvailableCoupons = db.Coupons.Where(c => !c.Products.Any()).ToList();
                    return View(model);
                }
            }
        }

        // GET: Orders/OrderSuccess/5
        public ActionResult OrderSuccess(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var order = db.Orders.Include("OrderDetails.Product").SingleOrDefault(o => o.OrderID == id);
            if (order == null) return HttpNotFound();

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
                    var order = db.Orders.Include(o => o.OrderDetails).SingleOrDefault(o => o.OrderID == orderId);
                    if (order != null)
                    {
                        if (vnp_ResponseCode == "00")
                        {
                            order.PaymentStatus = "Đã thanh toán";
                            db.SaveChanges();

                            Session.Remove("Cart");
                            Session.Remove("VoucherDiscount");
                            Session.Remove("BuyNowTempCart");
                            Session.Remove("ShippingFee");

                            // ✅ FIX LỖI 3: Loại bỏ TempData thông báo trùng lặp
                            TempData["Message"] = "Thanh toán đơn hàng qua cổng VNPay thành công!";
                            return RedirectToAction("OrderSuccess", new { id = orderId });
                        }
                        else
                        {
                            order.PaymentStatus = "Thất bại";
                            order.OrderStatus = "Đã hủy";

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
                            }

                            db.Entry(order).State = EntityState.Modified;
                            db.SaveChanges();

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