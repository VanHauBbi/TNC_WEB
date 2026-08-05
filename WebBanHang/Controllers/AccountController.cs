using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using WebBanHang.Models.ViewModel;
using WebBanHang.Models;
using System.Data.Entity;
using System.Security.Cryptography;
using System.Text;
using WebBanHang.Security;
using WebBanHang.Services;

namespace WebBanHang.Controllers
{
    public class AccountController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // ==========================================================
        // REGISTER
        // ==========================================================

        // GET: Account/Register
        public ActionResult Register()
        {
            return View(new RegisterVM());
        }

        // POST: Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(RegisterVM model)
        {
            if (ModelState.IsValid)
            {
                // 1. CHUẨN HÓA DỮ LIỆU ĐẦU VÀO
                var username = model.Username.Trim();
                var email = model.CustomerEmail.Trim();
                var phone = model.CustomerPhone.Trim();

                // 2. KIỂM TRA TRÙNG LẶP
                // SQL Server của dự án dùng collation không phân biệt hoa/thường; tránh overload
                // StringComparison vì EF6 không dịch được overload đó sang SQL.
                var existingUser = db.Users.SingleOrDefault(u => u.Username == username);
                if (existingUser != null)
                {
                    TempData["ErrorMessage"] = "Tên đăng nhập này đã tồn tại.";
                    return View(model);
                }

                var existingCustomer = db.Customers.SingleOrDefault(c => c.CustomerEmail == email || c.CustomerPhone == phone);

                if (existingCustomer != null)
                {
                    if (string.Equals(existingCustomer.CustomerEmail, email, StringComparison.OrdinalIgnoreCase))
                    {
                        TempData["ErrorMessage"] = "Địa chỉ Email này đã được sử dụng.";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Số điện thoại này đã được sử dụng.";
                    }
                    return View(model);
                }

                // 3. TẠO VÀ LƯU BẢN GHI
                var user = new User
                {
                    Username = username,
                    Password = PasswordHasher.Hash(model.Password),
                    UserRole = "C"
                };
                db.Users.Add(user);

                var customer = new Customer
                {
                    CustomerName = model.CustomerName.Trim(),
                    CustomerEmail = email,
                    CustomerPhone = phone,
                    CustomerAddress = model.CustomerAddress.Trim(),
                    Username = username,
                    IsActive = true
                };
                db.Customers.Add(customer);

                // --- BỌC try...catch VÀO ĐÂY ---
                try
                {
                    db.SaveChanges(); // Lỗi xảy ra ở dòng này (Line 117 cũ)
                }
                catch (System.Data.Entity.Validation.DbEntityValidationException ex)
                {
                    // Đây là đoạn code debug
                    var errors = new System.Text.StringBuilder();
                    foreach (var validationErrors in ex.EntityValidationErrors)
                    {
                        foreach (var validationError in validationErrors.ValidationErrors)
                        {
                            errors.AppendFormat("Property: {0} Error: {1} | ",
                                                validationError.PropertyName,
                                                validationError.ErrorMessage);
                        }
                    }

                    // Đặt breakpoint ở dòng dưới và chạy lại trang.
                    // Khi bị lỗi, rê chuột vào biến "errorDetails" để xem lỗi là gì.
                    string errorDetails = errors.ToString();

                    System.Diagnostics.Trace.TraceError("Lỗi validation đăng ký: " + errorDetails);
                    TempData["ErrorMessage"] = "Không thể tạo tài khoản. Vui lòng kiểm tra lại thông tin.";
                    return View(model);
                }
                // --- KẾT THÚC try...catch ---

                TempData["SuccessMessage"] = "Đăng ký thành công! Hãy đăng nhập để tiếp tục.";
                return RedirectToAction("Login", "Account");
            }

            return View(model);
        }


        // ==========================================================
        // LOGIN
        // ==========================================================

        // Get: Account/Login
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginVM());
        }

        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(LoginVM model, string returnUrl)
        {
            if (ModelState.IsValid)
            {
                // Test Case Login009_Trim: Tự động loại bỏ khoảng trắng
                string input = model.UserName.Trim();

                // BƯỚC 1: Tìm Username chuẩn
                // Test Case Login010: Không phân biệt hoa thường
                var customerByInput = db.Customers.SingleOrDefault(c =>
                    c.CustomerEmail == input || c.CustomerPhone == input || c.Username == input);

                string usernameToFind = customerByInput != null ? customerByInput.Username : input;

                // BƯỚC 2: Tìm tài khoản User
                // Test Case Login010: Không phân biệt hoa thường
                var user = db.Users.SingleOrDefault(u => u.Username == usernameToFind);

                // BƯỚC 3: XỬ LÝ ĐĂNG NHẬP

                // Test Case Login003: Sai username (không tồn tại)
                if (user == null)
                {
                    TempData["ErrorMessage"] = "Tên đăng nhập hoặc mật khẩu không đúng.";
                    return View(model);
                }

                if (PasswordHasher.Verify(model.Password, user.Password, out var needsUpgrade))
                {
                    Customer customerData = null;
                    if (user.UserRole == "C")
                    {
                        customerData = db.Customers.SingleOrDefault(c => c.Username == user.Username);
                        if (customerData == null || !customerData.IsActive)
                        {
                            TempData["ErrorMessage"] = "Tài khoản đã bị khóa hoặc không còn hoạt động.";
                            return View(model);
                        }
                    }

                    if (needsUpgrade)
                    {
                        user.Password = PasswordHasher.Hash(model.Password);
                        db.SaveChanges();
                    }

                    // ĐĂNG NHẬP THÀNH CÔNG
                    Session["UserName"] = user.Username;
                    Session["UserRole"] = user.UserRole;
                    if (customerData != null)
                    {
                        Session["CustomerID"] = customerData.CustomerID;
                        MergeAnonymousBehavior(customerData.CustomerID);
                        RestoreCart(customerData.CustomerID);
                    }

                    FormsAuthentication.SetAuthCookie(user.Username, model.RememberMe);

                    // Test Case Login017: Vai trò Admin
                    if (user.UserRole == "A")
                    {
                        return RedirectToAction("Index", "Home", new { Area = "Admin" });
                    }

                    // FIX: Xử lý returnUrl để quay lại trang cũ
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }

                    // Test Case Login001 & Login016: Vai trò Khách hàng (mặc định)
                    return RedirectToAction("Index", "Home");
                }
                else
                {
                    // MẬT KHẨU SAI (Test Case Login002)
                    // Đã bỏ logic tăng bộ đếm
                    TempData["ErrorMessage"] = "Tên đăng nhập hoặc mật khẩu không đúng.";
                    return View(model);
                }
            }

            // ModelState không hợp lệ
            return View(model);
        }

        // GET: /Account/ForgotPassword
        public ActionResult ForgotPassword()
        {
            return View(new ForgotPasswordVM());
        }
        // ==========================================================
        // FORGOT PASSWORD - STEP 1: YÊU CẦU RESET (Đã cập nhật)
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ForgotPassword(ForgotPasswordVM model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string input = model.UserIdentifier.Trim();
            var customer = db.Customers.SingleOrDefault(c =>
                c.CustomerEmail == input || c.CustomerPhone == input || c.Username == input);

            string usernameToFind = customer != null ? customer.Username : input;
            var user = db.Users.SingleOrDefault(u => u.Username == usernameToFind);

            // Luôn trả cùng một thông báo để không làm lộ tài khoản có tồn tại hay không.
            if (user != null && customer != null && !string.IsNullOrWhiteSpace(customer.CustomerEmail))
            {
                var rawToken = GenerateResetToken();
                user.ResetPasswordToken = HashResetToken(rawToken);
                user.ResetTokenExpiry = DateTime.UtcNow.AddMinutes(30);
                db.SaveChanges();

                var resetUrl = Url.Action("ResetPassword", "Account",
                    new { username = user.Username, token = rawToken }, Request.Url.Scheme);

                try
                {
                    new EmailService().SendPasswordReset(customer.CustomerEmail, resetUrl);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError("Không gửi được email reset: " + ex.Message);
                }
            }

            TempData["SuccessMessage"] = "Nếu thông tin hợp lệ, liên kết đặt lại mật khẩu sẽ được gửi đến email của bạn.";
            return RedirectToAction("ForgotPassword");
        }


        // ==========================================================
        // FORGOT PASSWORD - STEP 2: ĐẶT LẠI MẬT KHẨU (Đã cập nhật)
        // ==========================================================

        public ActionResult ResetPassword(string username, string token)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "Liên kết đặt lại mật khẩu không hợp lệ.";
                return RedirectToAction("Login");
            }

            var user = db.Users.SingleOrDefault(u => u.Username == username);
            if (!IsValidResetToken(user, token))
            {
                TempData["ErrorMessage"] = "Liên kết đặt lại mật khẩu đã hết hạn hoặc không hợp lệ.";
                return RedirectToAction("Login");
            }

            return View(new ResetPasswordVM { Username = username, Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ResetPassword(ResetPasswordVM model)
        {
            if (!ModelState.IsValid)
            {
                return View(model); // Test Case ForgotPW04, 05, 06
            }

            var user = db.Users.SingleOrDefault(u => u.Username == model.Username);
            if (!IsValidResetToken(user, model.Token))
            {
                TempData["ErrorMessage"] = "Liên kết đặt lại mật khẩu đã hết hạn hoặc không hợp lệ.";
                return RedirectToAction("Login");
            }

            if (PasswordHasher.Verify(model.NewPassword, user.Password, out _))
            {
                ModelState.AddModelError("", "Mật khẩu mới không được trùng với mật khẩu cũ.");
                return View(model);
            }

            user.Password = PasswordHasher.Hash(model.NewPassword);
            user.ResetPasswordToken = null;
            user.ResetTokenExpiry = null;
            db.SaveChanges();

            // Test Case ForgotPW01 & ForgotPW08
            TempData["SuccessMessage"] = "Đổi mật khẩu thành công! Bạn có thể đăng nhập ngay bây giờ.";
            return RedirectToAction("Login");
        }

        private void RestoreCart(int customerId)
        {
            var dbCart = db.Carts.Include(c => c.CartItems).SingleOrDefault(c => c.CustomerID == customerId);
            var sessionCart = new WebBanHang.Models.ViewModel.Cart();
            if (dbCart != null)
            {
                foreach (var dbItem in dbCart.CartItems)
                {
                    var product = db.Products.Include(p => p.Category).Include(p => p.Coupons)
                        .SingleOrDefault(p => p.ProductID == dbItem.ProductID);
                    if (product == null) continue;

                    decimal discountPercent;
                    var price = WebBanHang.Utilities.PriceHelper.GetDiscountedPrice(product, out discountPercent);
                    sessionCart.AddItem(product.ProductID, product.ProductImage, product.ProductName,
                        Math.Max(0, price), product.ProductPrice, dbItem.Quantity, product.Category?.CategoryName);
                }
            }
            Session["Cart"] = sessionCart;
        }

        private void MergeAnonymousBehavior(int customerId)
        {
            var sessionId = Session.SessionID;
            var anonymousLogs = db.UserBehaviorLogs
                .Where(x => x.CustomerID == null && x.SessionID == sessionId)
                .ToList();
            foreach (var log in anonymousLogs) log.CustomerID = customerId;
            if (anonymousLogs.Any()) db.SaveChanges();
        }

        private static string GenerateResetToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return HttpServerUtility.UrlTokenEncode(bytes);
        }

        private static string HashResetToken(string token)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        }

        private static bool IsValidResetToken(User user, string token)
        {
            return user != null
                   && user.ResetTokenExpiry.HasValue
                   && user.ResetTokenExpiry.Value >= DateTime.UtcNow
                   && string.Equals(user.ResetPasswordToken, HashResetToken(token), StringComparison.Ordinal);
        }

        // ==========================================================
        // LOGOUT & HELPERS
        // ==========================================================

        //GET: Account/Logout
        [HttpGet]
        public ActionResult Logout()
        {
            // Test Case Login014
            FormsAuthentication.SignOut();
            Session.Clear();
            Session.Abandon();
            return RedirectToAction("Login", "Account");
        }

        public JsonResult CheckLogin()
        {
            return Json(new { isLogin = Session["UserName"] != null }, JsonRequestBehavior.AllowGet);
        }

        // GET: Account/PurchaseHistory
        public ActionResult PurchaseHistory()
        {
            // Kiểm tra đăng nhập
            if (Session["CustomerID"] == null)
            {
                return RedirectToAction("Login", "Account");
            }

            int customerId = (int)Session["CustomerID"];

            using (var db = new WebBanHang.Models.MyStoreEntities())
            {
                // Lấy danh sách đơn hàng của người dùng hiện tại, sắp xếp mới nhất lên đầu
                var orders = db.Orders
                               .Where(o => o.CustomerID == customerId)
                               .OrderByDescending(o => o.OrderDate)
                               .ToList();

                return View(orders);
            }
        }

        public ActionResult MyVouchers()
        {
            if (Session["CustomerID"] == null)
                return RedirectToAction("Login", new { returnUrl = Url.Action("MyVouchers", "Account") });

            try
            {
                var vouchers = new MarketingSellingService(db).GetCustomerVouchers((int)Session["CustomerID"], true);
                return View(vouchers);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Chức năng voucher chưa sẵn sàng: " + ex.Message;
                return View(new System.Collections.Generic.List<PersonalVoucherVM>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UseVoucher(int id, int productId)
        {
            if (Session["CustomerID"] == null) return RedirectToAction("Login");
            new MarketingSellingService(db).TrackVoucherClick((int)Session["CustomerID"], id);
            return RedirectToAction("ProductDetail", "Home", new { id = productId });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
