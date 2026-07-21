namespace WebBanHang.Models.ViewModel
{
    public class CartItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public string CategoryName { get; set; } 
        public decimal OriginalPrice { get; set; }
        public decimal UnitPrice { get; set; }
        public string ProductImage { get; set; }
        public int StockQuantity { get; set; }

        public int ActiveCouponID { get; set; }
        public int ActiveCouponLimit { get; set; }

        public int DiscountableQuantity { get; set; }

        public decimal TotalPrice
        {
            get
            {
                if (OriginalPrice <= UnitPrice) return Quantity * UnitPrice;

                int applicableQty = System.Math.Min(Quantity, DiscountableQuantity);
                int remainingQty = Quantity - applicableQty;

                return (applicableQty * UnitPrice) + (remainingQty * OriginalPrice);
            }
        }
    }
}