using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using WebBanHang.Models.ViewModel;
using WebBanHang.Models;
using System.Data.Entity;
// THÊM MỚI: Cần thiết cho việc Hashing mật khẩu
using System.Security.Cryptography;
using System.Text;

namespace _23DH110809_MyStore.Controllers
{
    public class AccountController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        // ==========================================================
        // HASHING HELPER
        // ==========================================================

        /// <summary>
        /// Băm mật khẩu sử dụng SHA256
        /// </summary>
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                // Chuyển mật khẩu thành mảng byte
                byte[] bytes = Encoding.UTF8.GetBytes(password);
                // Băm mảng byte
                byte[] hash = sha256.ComputeHash(bytes);
                // Chuyển mảng byte đã băm thành chuỗi hex
                StringBuilder result = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    result.Append(hash[i].ToString("x2"));
                }
                return result.ToString();
            }
        }


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
                var existingUser = db.Users.SingleOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (existingUser != null)
                {
                    TempData["ErrorMessage"] = "Tên đăng nhập này đã tồn tại.";
                    return View(model);
                }

                var existingCustomer = db.Customers.SingleOrDefault(c =>
                    c.CustomerEmail.Equals(email, StringComparison.OrdinalIgnoreCase) ||
                    c.CustomerPhone == phone);

                if (existingCustomer != null)
                {
                    if (existingCustomer.CustomerEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
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
                    Password = HashPassword(model.Password),
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

                    // Gửi lỗi chi tiết ra màn hình để xem
                    TempData["ErrorMessage"] = "Lỗi validation: " + errorDetails;
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
        public ActionResult Login()
        {
            return View(new LoginVM());
        }

        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(LoginVM model)
        {
            if (ModelState.IsValid)
            {
                // Test Case Login009_Trim: Tự động loại bỏ khoảng trắng
                string input = model.UserName.Trim();

                // Hash mật khẩu nhập vào để so sánh
                string hashedPassword = HashPassword(model.Password);

                // BƯỚC 1: Tìm Username chuẩn
                // Test Case Login010: Không phân biệt hoa thường
                var customerByInput = db.Customers.SingleOrDefault(c =>
                    c.CustomerEmail.Equals(input, StringComparison.OrdinalIgnoreCase) ||
                    c.CustomerPhone == input ||
                    c.Username.Equals(input, StringComparison.OrdinalIgnoreCase));

                string usernameToFind = customerByInput != null ? customerByInput.Username : input;

                // BƯỚC 2: Tìm tài khoản User
                // Test Case Login010: Không phân biệt hoa thường
                var user = db.Users.SingleOrDefault(u => u.Username.Equals(usernameToFind, StringComparison.OrdinalIgnoreCase));

                // BƯỚC 3: XỬ LÝ ĐĂNG NHẬP

                // Test Case Login003: Sai username (không tồn tại)
                if (user == null)
                {
                    TempData["ErrorMessage"] = "Tên đăng nhập hoặc mật khẩu không đúng.";
                    return View(model);
                }

                // *** ĐÃ LOẠI BỎ LOGIC Login002_Lock TẠI ĐÂY ***

                // BƯỚC 4: Tải dữ liệu Customer và kiểm tra khóa (Admin)
                Customer customerData = null;
                if (user.UserRole == "C")
                {
                    customerData = db.Customers.SingleOrDefault(c => c.Username == user.Username);

                    // Test Case Login015: Tài khoản bị Admin khóa
                    if (customerData == null || customerData.IsActive == false)
                    {
                        TempData["ErrorMessage"] = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ Admin để được hỗ trợ!";
                        return View(model);
                    }
                }

                // BƯỚC 5: KIỂM TRA MẬT KHẨU

                // Test Case Login002: Sai mật khẩu
                if (user.Password == hashedPassword)
                {
                    // ĐĂNG NHẬP THÀNH CÔNG
                    Session["UserName"] = user.Username;
                    Session["UserRole"] = user.UserRole;
                    if (customerData != null)
                    {
                        Session["CustomerID"] = customerData.CustomerID;
                    }

                    // Test Case Login018: Cookie Remember Me
                    if (model.RememberMe)
                    {
                        FormsAuthentication.SetAuthCookie(user.Username, true);
                    }

                    // Test Case Login017: Vai trò Admin
                    if (user.UserRole == "A")
                    {
                        return RedirectToAction("Index", "Home", new { Area = "Admin" });
                    }

                    // Test Case Login001 & Login016: Vai trò Khách hàng
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

        // (Hãy đảm bảo bạn vẫn giữ 2 action GET /ForgotPassword và hàm HashPassword)
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

            // 1. Tìm User (logic giữ nguyên)
            string input = model.UserIdentifier.Trim();
            var customer = db.Customers.SingleOrDefault(c =>
                c.CustomerEmail.Equals(input, StringComparison.OrdinalIgnoreCase) ||
                c.CustomerPhone == input ||
                c.Username.Equals(input, StringComparison.OrdinalIgnoreCase));

            string usernameToFind = customer != null ? customer.Username : input;
            var user = db.Users.SingleOrDefault(u => u.Username.Equals(usernameToFind, StringComparison.OrdinalIgnoreCase));

            // 2. Nếu không tìm thấy, báo lỗi (giữ nguyên)
            if (user == null)
            {
                TempData["ErrorMessage"] = "Thông tin không hợp lệ hoặc tài khoản không tồn tại.";
                return View(model);
            }

            // 3. THAY ĐỔI: 
            // Thay vì tạo Token, chúng ta lưu Username vào TempData
            // và chuyển hướng TRỰC TIẾP.
            TempData["ResetUser"] = user.Username; // Lưu Username an toàn trên server
            return RedirectToAction("ResetPassword");
        }


        // ==========================================================
        // FORGOT PASSWORD - STEP 2: ĐẶT LẠI MẬT KHẨU (Đã cập nhật)
        // ==========================================================

        // GET: /Account/ResetPassword (Không cần Token nữa)
        public ActionResult ResetPassword()
        {
            // 1. KIỂM TRA: Người dùng có đi từ Bước 1 không?
            if (TempData["ResetUser"] == null)
            {
                // Nếu gõ URL trực tiếp, đá về trang Login
                TempData["ErrorMessage"] = "Phiên làm việc không hợp lệ.";
                return RedirectToAction("Login");
            }

            // 2. Lấy Username từ TempData
            string username = TempData["ResetUser"].ToString();

            // 3. Gửi Username qua ViewModel
            var model = new ResetPasswordVM
            {
                Username = username
            };

            // 4. Giữ lại TempData để dùng cho action POST (bảo mật)
            TempData.Keep("ResetUser");

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ResetPassword(ResetPasswordVM model)
        {
            if (!ModelState.IsValid)
            {
                return View(model); // Test Case ForgotPW04, 05, 06
            }

            // 1. KIỂM TRA BẢO MẬT:
            // Đảm bảo người dùng này vẫn trong phiên reset hợp lệ
            // và không cố tình thay đổi Username trong form
            if (TempData["ResetUser"] == null || TempData["ResetUser"].ToString() != model.Username)
            {
                TempData["ErrorMessage"] = "Phiên làm việc đã hết hạn hoặc không hợp lệ. Vui lòng thử lại.";
                return RedirectToAction("Login");
            }

            // 2. Tìm User
            var user = db.Users.SingleOrDefault(u => u.Username == model.Username);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Tài khoản không tồn tại.";
                return RedirectToAction("Login");
            }

            // 3. Kiểm tra mật khẩu cũ (Test Case ForgotPW09)
            string newHashedPassword = HashPassword(model.NewPassword);
            if (user.Password == newHashedPassword)
            {
                ModelState.AddModelError("", "Mật khẩu mới không được trùng với mật khẩu cũ.");
                return View(model);
            }

            // 4. Cập nhật mật khẩu mới (Test Case ForgotPW07)
            user.Password = newHashedPassword;

            // 5. Xóa Token (nếu bạn vẫn dùng cột đó, không thì bỏ qua)
            user.ResetPasswordToken = null;
            user.ResetTokenExpiry = null;

            db.SaveChanges();

            // 6. Xóa TempData sau khi hoàn tất
            TempData.Remove("ResetUser");

            // Test Case ForgotPW01 & ForgotPW08
            TempData["SuccessMessage"] = "Đổi mật khẩu thành công! Bạn có thể đăng nhập ngay bây giờ.";
            return RedirectToAction("Login");
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

        public ActionResult PurchaseHistory()
        {
            if (Session["CustomerID"] == null)
            {
                TempData["Message"] = "Vui lòng đăng nhập để xem lịch sử mua hàng.";
                return RedirectToAction("Login");
            }

            int customerId = (int)Session["CustomerID"];

            var orders = db.Orders
                           .Include(o => o.OrderDetails)
                           .Where(o => o.CustomerID == customerId)
                           .OrderByDescending(o => o.OrderDate)
                           .ToList();

            return View(orders);
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