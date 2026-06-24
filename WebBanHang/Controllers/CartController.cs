using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using WebBanHang.Models.ViewModel;
using WebBanHang.Models;
using System.Data.Entity;
using WebBanHang.Utilities;

namespace WebBanHang.Controllers
{
    public class BuildPCItemRequest
    {
        public int ProductID { get; set; }
        public int Quantity { get; set; }
    } 
    public class CartController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();
        private CartService GetCartService()
        {
            return new CartService(Session);
        }

        public ActionResult Index()
        {
            var cart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;
            if (cart == null) cart = new WebBanHang.Models.ViewModel.Cart();

            using (var tempDb = new WebBanHang.Models.MyStoreEntities())
            {
                foreach (var item in cart.Items)
                {
                    var product = tempDb.Products.Include("Coupons").SingleOrDefault(p => p.ProductID == item.ProductID);
                    if (product != null)
                    {
                        item.StockQuantity = product.StockQuantity;

                        if (item.OriginalPrice > item.UnitPrice)
                        {
                            var activeCoupon = product.Coupons
                                .Where(c => c.ExpiryDate > DateTime.Now)
                                .OrderByDescending(c => c.DiscountPercentage ?? (c.MaxDiscountAmount ?? 0))
                                .FirstOrDefault();

                            if (activeCoupon != null)
                            {
                                // CHỈ NẠP DỮ LIỆU ĐỂ TRUYỀN RA VIEW
                                item.ActiveCouponID = activeCoupon.CouponID;
                                item.ActiveCouponLimit = activeCoupon.UsageLimit;
                            }
                        }
                    }
                }
            }
            return View(cart);
        }
        // THÊM SẢN PHẨM VÀO GIỎ HÀNG
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

                // SỬA ĐỔI: Dùng Include() để nạp dữ liệu Coupons
                var product = db.Products.Include(p => p.Coupons).SingleOrDefault(p => p.ProductID == id);

                if (product == null)
                    return Json(new { success = false, message = "Sản phẩm không tồn tại." });

                // TÍNH TOÁN GIÁ ĐỘNG: Lấy giá đã giảm (nếu có)
                decimal discountPercent;
                decimal finalUnitPrice = PriceHelper.GetDiscountedPrice(product, out discountPercent);

                var cartService = GetCartService();
                var cart = cartService.GetCart();

                // Truyền finalUnitPrice làm UnitPrice, và product.ProductPrice làm OriginalPrice
                cart.AddItem(product.ProductID, product.ProductImage, product.ProductName,
                    finalUnitPrice, product.ProductPrice, quantity, product.Category?.CategoryName);

                Session["Cart"] = cart;

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
                // Log error
                System.Diagnostics.Debug.WriteLine("Lỗi: " + ex.Message);
                TempData["ErrorMessage"] = "Có lỗi xảy ra. Vui lòng thử lại.";
                return RedirectToAction("Index");
            }
        }

        // XỬ LÝ THÊM CẤU HÌNH BUILD PC VÀO GIỎ HÀNG
        [HttpPost]
        public ActionResult AddBuildPC(List<BuildPCItemRequest> items)
        {
            var cartService = GetCartService();
            var cart = cartService.GetCart();

            foreach (var item in items)
            {
                var product = db.Products.Find(item.ProductID);
                if (product != null)
                {
                    cart.AddItem(product.ProductID, product.ProductImage, product.ProductName,
                                 product.ProductPrice, product.ProductPrice, item.Quantity, "");
                }
            }
            Session["Cart"] = cart;
            return Json(new { success = true });
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
            var cart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;
            int count = cart?.Items.Sum(i => i.Quantity) ?? 0;
            return Json(new { count = count }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult GetMiniCart()
        {
            var cart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;
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
                            <span style='color:#666;font-size:13px;'>
                                {i.Quantity} × 
                                {(i.OriginalPrice > i.UnitPrice ? $"<del style='color:#999;'>{i.OriginalPrice:N0}</del> " : "")}
                                <strong style='color:#dc3545;'>{i.UnitPrice:N0} VNĐ</strong>
                            </span>
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

        // MUA NGAY
        public ActionResult BuyNow(int id)
        {
            if (Session["CustomerID"] == null)
            {
                TempData["Message"] = "Vui lòng đăng nhập để sử dụng chức năng Mua ngay.";
                return RedirectToAction("Login", "Account");
            }

            var currentCart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;
            if (currentCart != null && currentCart.Items.Any())
            {
                Session["BuyNowTempCart"] = currentCart;
            }

            // SỬA ĐỔI: Dùng Include() để nạp dữ liệu Coupons
            var product = db.Products.Include(p => p.Coupons).SingleOrDefault(p => p.ProductID == id);

            if (product == null)
            {
                TempData["Error"] = "Sản phẩm không tồn tại.";
                return RedirectToAction("Index", "Home");
            }

            // TÍNH TOÁN GIÁ ĐỘNG: Lấy giá đã giảm (nếu có)
            decimal discountPercent;
            decimal finalUnitPrice = PriceHelper.GetDiscountedPrice(product, out discountPercent);

            var buyNowCart = new WebBanHang.Models.ViewModel.Cart();

            // Truyền finalUnitPrice làm UnitPrice, và product.ProductPrice làm OriginalPrice
            buyNowCart.AddItem(product.ProductID, product.ProductImage, product.ProductName,
                                finalUnitPrice, product.ProductPrice, 1, product.Category?.CategoryName);

            Session["Cart"] = buyNowCart;

            return RedirectToAction("Checkout", "Orders");
        }

        [HttpPost]
        public JsonResult ApplyCoupon(string couponCode)
        {
            if (string.IsNullOrEmpty(couponCode))
            {
                return Json(new { success = false, message = "Vui lòng nhập mã giảm giá." });
            }

            try
            {
                var coupon = db.Coupons.Include(c => c.Products)
                                       .FirstOrDefault(c => c.Code == couponCode.Trim().ToUpper());

                if (coupon == null)
                    return Json(new { success = false, message = "Mã giảm giá không tồn tại." });

                if (coupon.ExpiryDate <= DateTime.Now)
                    return Json(new { success = false, message = "Mã giảm giá này đã hết hạn sử dụng." });

                if (coupon.UsageLimit <= 0)
                    return Json(new { success = false, message = "Mã giảm giá này đã hết lượt sử dụng." });

                // SỬA TẠI ĐÂY: Chặn ngay nếu đây là mã Sản phẩm cụ thể
                if (coupon.Products != null && coupon.Products.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "Mã này là ưu đãi riêng cho sản phẩm và đã được trừ thẳng vào giá bán. Vui lòng chọn mã giảm giá toàn đơn!"
                    });
                }

                var cart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;
                if (cart == null || !cart.Items.Any())
                    return Json(new { success = false, message = "Giỏ hàng trống, không thể áp dụng mã." });

                decimal totalDiscount = 0;
                decimal cartTotal = cart.TotalValue();

                // DO ĐÃ CHẶN MÃ CỤC BỘ Ở TRÊN, LOGIC DƯỚI ĐÂY CHỈ DÀNH CHO MÃ TOÀN ĐƠN
                if (coupon.DiscountPercentage.HasValue && coupon.DiscountPercentage.Value > 0)
                {
                    totalDiscount = cartTotal * (coupon.DiscountPercentage.Value / 100m);
                }

                // Khống chế mức giảm tối đa
                if (coupon.MaxDiscountAmount.HasValue && coupon.MaxDiscountAmount.Value > 0)
                {
                    if (totalDiscount == 0 || totalDiscount > coupon.MaxDiscountAmount.Value)
                    {
                        totalDiscount = coupon.MaxDiscountAmount.Value;
                    }
                }

                return Json(new
                {
                    success = true,
                    message = "Áp dụng mã giảm giá toàn đơn thành công!",
                    discountAmount = totalDiscount
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi xử lý: " + ex.Message });
            }
        }

        [HttpPost]
        public ActionResult ProceedToCheckout(List<int> selectedIds)
        {
            var currentCart = Session["Cart"] as WebBanHang.Models.ViewModel.Cart;

            if (currentCart == null || !currentCart.Items.Any() || selectedIds == null || selectedIds.Count == 0)
            {
                return Json(new { success = false, message = "Vui lòng chọn ít nhất 1 sản phẩm để thanh toán." });
            }

            // Lưu trữ giỏ hàng tổng vào Session tạm để khôi phục sau khi đặt hàng
            Session["BuyNowTempCart"] = currentCart;

            // Khởi tạo giỏ hàng mới chỉ chứa các sản phẩm được chọn
            var checkoutCart = new WebBanHang.Models.ViewModel.Cart();

            foreach (var id in selectedIds)
            {
                var item = currentCart.Items.FirstOrDefault(i => i.ProductID == id);
                if (item != null)
                {
                    checkoutCart.AddItem(item.ProductID, item.ProductImage, item.ProductName, item.UnitPrice, item.OriginalPrice, item.Quantity, "");
                }
            }

            // Ghi đè giỏ hàng hiện tại bằng giỏ hàng thanh toán
            Session["Cart"] = checkoutCart;

            return Json(new { success = true, redirectUrl = Url.Action("Checkout", "Orders") });
        }
    }
}