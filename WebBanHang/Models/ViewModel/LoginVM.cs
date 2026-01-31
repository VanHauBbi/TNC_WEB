using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace WebBanHang.Models.ViewModel
{
    public class LoginVM
    {
        [Display(Name = "Số điện thoại/ Email/ Tên đăng nhập")]
        [Required(ErrorMessage = "Vui lòng nhập Số điện thoại/ Email/ Tên đăng nhập")]
        public string UserName { get; set; }

        [Display(Name = "Mật khẩu")]
        [Required(ErrorMessage = "Vui lòng nhập Mật khẩu.")]
        [DataType(DataType.Password)]
        // THÊM MỚI: Khớp với Test Case Login008 (Password < 8 ký tự)
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
        public string Password { get; set; }
        public bool RememberMe { get; set; } // Lưu tài khoản
    }
}