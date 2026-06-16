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

            var model = new CheckoutVM
            {
                CartItems = cart.Items.ToList(),
                TotalAmount = cart.TotalValue(),
                OrderDate = DateTime.Now,
                PaymentStatus = "Chưa thanh toán",
                // Nạp toàn bộ danh sách Coupon để hiển thị lên Modal
                AvailableCoupons = db.Coupons.Where(c => !c.Products.Any()).OrderByDescending(c => c.CouponID).ToList(),
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

                    // 2. LƯU CHI TIẾT, TRỪ TỒN KHO & TRỪ VOUCHER SẢN PHẨM CỤ THỂ
                    foreach (var item in cart.Items)
                    {
                        var detail = new OrderDetail
                        {
                            OrderID = order.OrderID,
                            ProductID = item.ProductID,
                            Quantity = item.Quantity,
                            UnitPrice = item.UnitPrice
                        };
                        db.OrderDetails.Add(detail);

                        // Include(Coupons) để lấy danh sách mã của sản phẩm này
                        var productInDb = db.Products.Include(p => p.Coupons).SingleOrDefault(p => p.ProductID == item.ProductID);
                        if (productInDb != null)
                        {
                            // Trừ tồn kho
                            productInDb.StockQuantity -= item.Quantity;
                            if (productInDb.StockQuantity < 0) productInDb.StockQuantity = 0;

                            // Nếu Giá mua < Giá gốc, chắc chắn sản phẩm này đã được áp dụng Voucher cục bộ!
                            if (item.UnitPrice < item.OriginalPrice)
                            {
                                // Tìm mã cục bộ tốt nhất đang còn hạn/lượt để trừ đi 1 lượt
                                var appliedCoupon = productInDb.Coupons
                                    .Where(c => c.ExpiryDate > DateTime.Now && c.UsageLimit > 0)
                                    .OrderByDescending(c => c.DiscountPercentage ?? (c.MaxDiscountAmount ?? 0))
                                    .FirstOrDefault();

                                if (appliedCoupon != null)
                                {
                                    appliedCoupon.UsageLimit -= 1;
                                }
                            }
                        }
                    }

                    // 3. TRỪ VOUCHER TOÀN ĐƠN (Nếu khách có nhập ở Modal)
                    if (!string.IsNullOrEmpty(model.AppliedVoucherCode))
                    {
                        var globalCoupon = db.Coupons.SingleOrDefault(c => c.Code == model.AppliedVoucherCode);
                        if (globalCoupon != null && globalCoupon.UsageLimit > 0)
                        {
                            globalCoupon.UsageLimit -= 1;
                        }
                    }

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
