using System.ComponentModel.DataAnnotations;

namespace WebBanHang.Models.ViewModel
{
    public class ForgotPasswordVM
    {
        // Test Case ForgotPW11: Dùng chung 1 trường cho cả 3
        [Required(ErrorMessage = "Vui lòng nhập Email / SĐT / Tên đăng nhập")]
        [Display(Name = "Email / SĐT / Tên đăng nhập")]
        public string UserIdentifier { get; set; }
    }
}