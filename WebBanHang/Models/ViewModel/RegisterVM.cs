using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace WebBanHang.Models.ViewModel
{
    public class RegisterVM
    {
        [Required(ErrorMessage = "Vui lòng nhập Tên đăng nhập.")]
        [Display(Name = "Tên đăng nhập")]
        // CẬP NHẬT: Khớp Test Case Register16 (Báo lỗi khi nhập 15 ký tự -> max length là 14)
        [StringLength(14, MinimumLength = 4, ErrorMessage = "Tên đăng nhập phải dài từ 4 đến 14 ký tự.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập Mật khẩu.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải dài ít nhất 8 ký tự.")]
        public string Password { get; set; }


        [Required(ErrorMessage = "Vui lòng xác nhận Mật khẩu.")]
        [DataType(DataType.Password)]
        [Display(Name = "Xác nhận mật khẩu")]
        [Compare("Password", ErrorMessage = "Mật khẩu và xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; }


        [Required(ErrorMessage = "Vui lòng nhập Họ tên.")]
        [Display(Name = "Họ tên")]
        [StringLength(100, ErrorMessage = "Họ tên không được vượt quá 100 ký tự.")]
        // THÊM MỚI: Khớp Test Case Register17 (Không cho phép ký tự đặc biệt)
        // Regex này cho phép chữ cái (bao gồm tiếng Việt có dấu) và khoảng trắng.
        [RegularExpression(@"^[\p{L} ]+$", ErrorMessage = "Họ tên chỉ được chứa chữ cái và khoảng trắng.")]
        public string CustomerName { get; set; }


        [Required(ErrorMessage = "Vui lòng nhập Số điện thoại.")]
        [Display(Name = "Số điện thoại")]
        [DataType(DataType.PhoneNumber)]
        [RegularExpression(@"^(\+84|0)(3|5|7|8|9)\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string CustomerPhone { get; set; }


        [Required(ErrorMessage = "Vui lòng nhập Email.")]
        [Display(Name = "Email")]
        [EmailAddress(ErrorMessage = "Địa chỉ Email không hợp lệ.")]
        public string CustomerEmail { get; set; }


        [Required(ErrorMessage = "Vui lòng nhập Địa chỉ.")]
        [Display(Name = "Địa chỉ")]
        public string CustomerAddress { get; set; }
    }
}