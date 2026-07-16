using System.Collections.Generic;
using WebBanHang.Models;

namespace WebBanHang.Models.ViewModel
{
    public class ProductAIDetailVM
    {
        // Thông tin cơ bản của sản phẩm A
        public Product ProductInfo { get; set; }

        // Danh sách gợi ý được sắp xếp theo thuật toán Two-Phase (Ưu tiên Lợi nhuận cao nhất)
        public List<SmartRecommendation> TwoPhaseRecommendations { get; set; }

        // Danh sách gợi ý được sắp xếp theo thuật toán Apriori (Ưu tiên Độ tin cậy mua kèm cao nhất)
        public List<SmartRecommendation> AprioriRecommendations { get; set; }
    }
}