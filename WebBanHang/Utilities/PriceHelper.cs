using System;
using System.Linq;
using WebBanHang.Models;

namespace WebBanHang.Utilities
{
    public static class PriceHelper
    {
        // Hàm trả về Giá sau khi giảm và xuất ra (out) Phần trăm giảm để hiển thị
        public static decimal GetDiscountedPrice(Product p, out decimal discountPercent)
        {
            discountPercent = 0;

            // Kiểm tra xem sản phẩm có được liên kết với mã giảm giá nào không
            // (Yêu cầu bảng Product đã được EF map thuộc tính ICollection<Coupon> Coupons)
            if (p.Coupons == null || !p.Coupons.Any()) return p.ProductPrice;

            // Lọc ra các mã còn hạn và còn lượt sử dụng
            var activeCoupons = p.Coupons.Where(c => c.ExpiryDate > DateTime.Now && c.UsageLimit > 0).ToList();
            if (!activeCoupons.Any()) return p.ProductPrice;

            decimal minPrice = p.ProductPrice;
            decimal maxPercent = 0;

            // Duyệt qua các mã hợp lệ để tìm mã có lợi nhất cho khách hàng
            foreach (var c in activeCoupons)
            {
                decimal discountAmt = 0;
                decimal currentPercent = 0;

                if (c.DiscountPercentage.HasValue && c.DiscountPercentage.Value > 0)
                {
                    // Trường hợp 1: Giảm theo phần trăm
                    currentPercent = c.DiscountPercentage.Value;
                    discountAmt = p.ProductPrice * (currentPercent / 100m);

                    if (c.MaxDiscountAmount.HasValue && discountAmt > c.MaxDiscountAmount.Value)
                    {
                        discountAmt = c.MaxDiscountAmount.Value;
                        currentPercent = (discountAmt / p.ProductPrice) * 100m; // Tính lại % thực tế
                    }
                }
                else if (c.MaxDiscountAmount.HasValue)
                {
                    // Trường hợp 2: Giảm thẳng tiền mặt (Không có %)
                    discountAmt = c.MaxDiscountAmount.Value;
                    currentPercent = (discountAmt / p.ProductPrice) * 100m;
                }

                decimal priceAfter = p.ProductPrice - discountAmt;
                if (priceAfter < minPrice)
                {
                    minPrice = priceAfter;
                    maxPercent = currentPercent;
                }
            }

            discountPercent = Math.Ceiling(maxPercent);
            return minPrice;
        }
    }
}