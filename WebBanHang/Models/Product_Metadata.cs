using System;

namespace WebBanHang.Models
{
    public partial class Product
    {
        public string GetStatusLabel()
        {
            if (this.Status == 0 || this.ProductPrice <= 0m)
                return "COMING_SOON";

            if (this.Status == 1 && this.StockQuantity <= 0)
                return "OUT_OF_STOCK";

            if (this.Status == 1 && this.StockQuantity > 0)
                return "AVAILABLE";

            return "DISABLED";
        }
    }
}