using System.ComponentModel.DataAnnotations;

namespace WebBanHang.Models.ViewModel
{
    public class ResetPasswordVM
    {
        // THAY ĐỔI: Thêm Username (sẽ dùng làm trường ẩn)
        [Required]
        public string Username { get; set; }

        // Bỏ Token

        // Test Case ForgotPW02
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu mới")]
        public string NewPassword { get; set; }

        // Test Case ForgotPW06
        [DataType(DataType.Password)]
        [Display(Name = "Xác nhận mật khẩu mới")]
        [Compare("NewPassword", ErrorMessage = "Mật khẩu và xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; }
    }
}