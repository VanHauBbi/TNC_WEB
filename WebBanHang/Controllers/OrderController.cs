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
            var cart = Session["Cart"] as Cart; // ✅ Đúng kiểu

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
                PaymentStatus = "Chưa thanh toán"
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Checkout(CheckoutVM model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var cart = Session["Cart"] as Cart; // ✅ Đúng kiểu
            if (cart == null || !cart.Items.Any())
            {
                ModelState.AddModelError("", "Giỏ hàng của bạn đang trống!");
                return View(model);
            }

            int customerId = (int)Session["CustomerID"];

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
            }
            db.SaveChanges();

            // 1. Lấy giỏ hàng gốc (nếu có)
            var tempCart = Session["BuyNowTempCart"] as Cart;

            if (tempCart != null)
            {
                // 2. Nếu có giỏ hàng gốc (BuyNow), khôi phục nó và xóa Session tạm
                Session["Cart"] = tempCart;
                Session.Remove("BuyNowTempCart");
            }
            else
            {
                // 3. Nếu đây là giỏ hàng thường, chỉ xóa giỏ hàng hiện tại
                Session["Cart"] = null;
            }
            return RedirectToAction("OrderSuccess", new { id = order.OrderID });
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
