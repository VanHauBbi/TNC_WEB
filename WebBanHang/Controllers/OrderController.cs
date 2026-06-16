using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using WebBanHang.Models;
using WebBanHang.Models.ViewModel;

namespace WebBanHang.Controllers
{
    public class OrdersController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

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
                    // Lưu hóa đơn
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
                    db.SaveChanges();

                    decimal actualTotalOrderAmount = 0; // Biến tính tổng tiền thực tế chống F12 Hack

                    // 2. TÁCH DÒNG CHI TIẾT & TRỪ VOUCHER ĐA LƯỢNG
                    foreach (var item in cart.Items)
                    {
                        var productInDb = db.Products.Include(p => p.Coupons).SingleOrDefault(p => p.ProductID == item.ProductID);
                        if (productInDb != null)
                        {
                            productInDb.StockQuantity -= item.Quantity; // Trừ tồn kho
                            if (productInDb.StockQuantity < 0) productInDb.StockQuantity = 0;

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
                                    // Chốt số lượng hợp lệ và số dư thừa
                                    applicableQty = Math.Min(item.Quantity, appliedCoupon.UsageLimit);
                                    remainingQty = item.Quantity - applicableQty;

                                    // TRỪ ĐÚNG SỐ LƯỢT ĐÃ TIÊU HAO CỦA VOUCHER
                                    appliedCoupon.UsageLimit -= applicableQty;
                                }
                            }

                            // DÒNG 1: Nhóm sản phẩm được áp giá giảm (Nếu có)
                            if (applicableQty > 0)
                            {
                                db.OrderDetails.Add(new OrderDetail
                                {
                                    OrderID = order.OrderID,
                                    ProductID = item.ProductID,
                                    Quantity = applicableQty,
                                    UnitPrice = item.UnitPrice
                                });
                                actualTotalOrderAmount += (applicableQty * item.UnitPrice);
                            }

                            // DÒNG 2: Nhóm sản phẩm bị đẩy về giá gốc do vượt giới hạn voucher (Nếu có)
                            if (remainingQty > 0)
                            {
                                db.OrderDetails.Add(new OrderDetail
                                {
                                    OrderID = order.OrderID,
                                    ProductID = item.ProductID,
                                    Quantity = remainingQty,
                                    UnitPrice = item.OriginalPrice
                                });
                                actualTotalOrderAmount += (remainingQty * item.OriginalPrice);
                            }
                        }
                    }

                    // 3. Xử lý Voucher TOÀN ĐƠN và Cập nhật lại tổng tiền Hóa đơn
                    if (!string.IsNullOrEmpty(model.AppliedVoucherCode))
                    {
                        var globalCoupon = db.Coupons.SingleOrDefault(c => c.Code == model.AppliedVoucherCode);
                        if (globalCoupon != null && globalCoupon.UsageLimit > 0)
                        {
                            globalCoupon.UsageLimit -= 1;
                            // (Trừ tiền voucher toàn đơn vào biến actualTotalOrderAmount ở đây nếu bạn lưu số discount vào Session)
                            actualTotalOrderAmount -= Convert.ToDecimal(Session["VoucherDiscount"] ?? 0);
                        }
                    }

                    // Chốt tổng tiền an toàn 100% vào DB
                    order.TotalAmount = actualTotalOrderAmount;
                    db.SaveChanges();
                    transaction.Commit();

                    // Xóa giỏ hàng sau khi mua thành công
                    var tempCart = Session["BuyNowTempCart"] as WebBanHang.Models.ViewModel.Cart;
                    if (tempCart != null)
                    {
                        foreach (var boughtItem in cart.Items) { tempCart.RemoveItem(boughtItem.ProductID); }
                        Session["Cart"] = tempCart;
                        Session.Remove("BuyNowTempCart");
                    }
                    else
                    {
                        Session["Cart"] = null;
                    }

                    return RedirectToAction("OrderSuccess", new { id = order.OrderID });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    ModelState.AddModelError("", "Đã xảy ra lỗi hệ thống khi lưu đơn hàng. Vui lòng thử lại!");
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

    } 
}
