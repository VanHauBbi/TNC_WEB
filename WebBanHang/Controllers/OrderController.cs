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
                model.AvailableCoupons = db.Coupons.ToList(); // Load lại để tránh lỗi View
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                model.AvailableCoupons = db.Coupons.ToList();
                return View(model);
            }

            int customerId = (int)Session["CustomerID"];

            // KHỞI TẠO DATABASE TRANSACTION ĐỂ BẢO VỆ DỮ LIỆU
            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    // 1. Lưu hóa đơn
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
                    db.SaveChanges(); // Lưu lần 1 để lấy OrderID

                    // 2. Lưu chi tiết & Trừ Tồn Kho Sản Phẩm
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

                        // Trừ số lượng tồn kho
                        var productInDb = db.Products.Find(item.ProductID);
                        if (productInDb != null)
                        {
                            productInDb.StockQuantity -= item.Quantity;
                            if (productInDb.StockQuantity < 0) productInDb.StockQuantity = 0;
                        }
                    }

                    // 3. Trừ Lượt Sử Dụng Voucher (Nếu có)
                    if (!string.IsNullOrEmpty(model.AppliedVoucherCode))
                    {
                        var coupon = db.Coupons.SingleOrDefault(c => c.Code == model.AppliedVoucherCode);
                        if (coupon != null && coupon.UsageLimit > 0)
                        {
                            coupon.UsageLimit -= 1;
                        }
                    }

                    // Lưu toàn bộ thay đổi và chốt Transaction
                    db.SaveChanges();
                    transaction.Commit();

                    // XỬ LÝ DỌN DẸP SESSION GIỎ HÀNG (ĐÃ FIX LỖI)
                    var tempCart = Session["BuyNowTempCart"] as WebBanHang.Models.ViewModel.Cart;
                    if (tempCart != null)
                    {
                        // Xóa các sản phẩm vừa thanh toán thành công khỏi giỏ hàng gốc
                        foreach (var boughtItem in cart.Items)
                        {
                            tempCart.RemoveItem(boughtItem.ProductID);
                        }

                        // Khôi phục giỏ hàng gốc (lúc này chỉ còn lại các sản phẩm chưa mua)
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
                    // NẾU CÓ LỖI (Ví dụ: đứt mạng, lỗi SQL), TẤT CẢ SẼ ĐƯỢC HOÀN TÁC!
                    transaction.Rollback();
                    ModelState.AddModelError("", "Đã xảy ra lỗi khi lưu đơn hàng. Vui lòng thử lại!");
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
