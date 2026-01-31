using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using WebBanHang.Models.ViewModel;
using WebBanHang.Models;

namespace WebBanHang.Controllers

{
    public class CartController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();
        private CartService GetCartService()
        {
            return new CartService(Session);
        }
        //Hiển thị giỏ hàng k gom nhóm theo dm
        public ActionResult Index()
        {
            var cart = GetCartService().GetCart();
            return View(cart);
        }
        //thêm sp vào giỏ hàng
        [HttpPost]
        public ActionResult AddToCart(int id, int quantity)
        {
            try
            {
                if (Session["UserName"] == null)
                {
                    return Json(new
                    {
                        success = false,
                        redirectUrl = Url.Action("Login", "Account")
                    });
                }

                var product = db.Products.Find(id);
                if (product == null)
                    return Json(new { success = false, message = "Sản phẩm không tồn tại." });

                var cartService = GetCartService();
                var cart = cartService.GetCart(); // ✅ luôn lấy từ CartService (đúng kiểu Cart)

                cart.AddItem(product.ProductID, product.ProductImage, product.ProductName,
                    product.ProductPrice, quantity, product.Category?.CategoryName);

                Session["Cart"] = cart; // ✅ lưu lại đối tượng Cart (không phải List<CartItem>)

                var total = cart.Items.Sum(x => x.TotalPrice);
                var count = cart.Items.Sum(x => x.Quantity);

                return Json(new
                {
                    success = true,
                    message = "Đã thêm vào giỏ hàng!",
                    cartCount = count,
                    cartTotal = total.ToString("N0") + " VNĐ"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        //public ActionResult MiniCart()
        //{
        //    if (Session["UserName"] == null)
        //        return Content("<p>Vui lòng đăng nhập để xem giỏ hàng.</p>");

        //    var cart = Session["Cart"] as Cart;
        //    if (cart == null || !cart.Items.Any())
        //        return Content("<p>Không có sản phẩm nào trong giỏ hàng.</p>");

        //    string html = "";
        //    decimal total = 0;

        //    foreach (var item in cart.Items)
        //    {
        //        html += $"<div class='cart-item d-flex align-items-center mb-2'>" +
        //                    $"<img src='{item.ProductImage}' alt='{item.ProductName}' style='width:50px;height:50px;object-fit:cover;margin-right:10px;' />" +
        //                    $"<div>" +
        //                        $"<p class='m-0'>{item.ProductName}</p>" +
        //                        $"<small>{item.UnitPrice.ToString("N0")} VNĐ x {item.Quantity}</small>" +
        //                    $"</div>" +
        //                $"</div>";
        //        total += item.UnitPrice * item.Quantity;
        //    }

        //    html += $"<div class='cart-total mt-2'><strong>Tổng: {total.ToString("N0")} VNĐ</strong></div>";

        //    return Content(html);
        //}

        //Xóa
        public ActionResult RemoveFromCart(int id)
        {
            var cartService = GetCartService();
            cartService.GetCart().RemoveItem(id);
            return RedirectToAction("Index");
        }
        [HttpPost]
        public ActionResult UpdateQuantity(int id, int quantity)
        {
            var cartService = GetCartService();
            cartService.GetCart().UpdateQuantity(id, quantity);
            return RedirectToAction("Index");
        }

        //private List<CartItem> GetCart()
        //{
        //    var cart = Session["Cart"] as List<CartItem>;
        //    if (cart == null)
        //    {
        //        cart = new List<CartItem>();
        //        Session["Cart"] = cart;
        //    }
        //    return cart;
        //}

        //private void SaveCart(List<CartItem> cart)
        //{
        //    Session["Cart"] = cart;
        //}

        [HttpGet]
        public JsonResult GetCartCount()
        {
            var cart = Session["Cart"] as Cart;
            int count = cart?.Items.Sum(i => i.Quantity) ?? 0;
            return Json(new { count = count }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult GetMiniCart()
        {
            var cart = Session["Cart"] as Cart;
            if (cart == null || !cart.Items.Any())
            {
                return Json(new
                {
                    success = true,
                    html = "<p>Không có sản phẩm nào trong giỏ hàng.</p>",
                    total = "0 VNĐ",
                    count = 0
                }, JsonRequestBehavior.AllowGet);
            }

            string html = string.Join("", cart.Items.Select(i => $@"
                <div class='mini-cart-item' style='display: flex; align-items: center; justify-content: space-between; padding: 5px 0; border-bottom: 1px solid #eee;'>
                    <div style='display: flex; align-items: center;'>
                        <img src='{i.ProductImage}' alt='ảnh' style='width:40px;height:40px;border-radius:5px;margin-right:8px;' />
                        <div>
                            <span style='display:block;font-weight:500;'>{i.ProductName}</span>
                            <span style='color:#666;font-size:13px;'>{i.Quantity} × {i.UnitPrice:N0} VNĐ</span>
                        </div>
                    </div>
                </div>
            "));

            return Json(new
            {
                success = true,
                html = html,
                total = cart.Items.Sum(x => x.TotalPrice).ToString("N0") + " VNĐ",
                count = cart.Items.Sum(x => x.Quantity)
            }, JsonRequestBehavior.AllowGet);
        }
        public ActionResult BuyNow(int id)
        {
            // 1. Kiểm tra đăng nhập (Giữ nguyên)
            if (Session["CustomerID"] == null)
            {
                TempData["Message"] = "Vui lòng đăng nhập để sử dụng chức năng Mua ngay.";
                return RedirectToAction("Login", "Account");
            }

            // 2. LƯU GIỎ HÀNG HIỆN TẠI VÀO SESSION TẠM THỜI (BuyNowTempCart)
            var currentCart = Session["Cart"] as Cart;
            if (currentCart != null && currentCart.Items.Any())
            {
                // Lưu giỏ hàng cũ sang Session BuyNowTemp
                Session["BuyNowTempCart"] = currentCart;
            }

            // 3. TẠO GIỎ HÀNG MỚI (chỉ chứa 1 sản phẩm)
            var product = db.Products.Find(id);
            if (product == null)
            {
                TempData["Error"] = "Sản phẩm không tồn tại.";
                return RedirectToAction("Index", "Home");
            }

            var buyNowCart = new Cart();

            // SỬ DỤNG PHƯƠNG THỨC ADDITEM VỚI ĐỦ 6 THAM SỐ
            // (Đây là phương thức đã được xác định qua AddToCart)
            buyNowCart.AddItem(product.ProductID, product.ProductImage, product.ProductName,
                                product.ProductPrice, 1, product.Category?.CategoryName); // Quantity là 1

            // 4. THAY THẾ Session["Cart"] BẰNG GIỎ HÀNG MUA NGAY (Tạm thời)
            Session["Cart"] = buyNowCart;

            // 5. Chuyển hướng thẳng đến trang Thanh toán
            return RedirectToAction("Checkout", "Orders");
        }
    }
}