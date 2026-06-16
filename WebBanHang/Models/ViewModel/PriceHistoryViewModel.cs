using System;

namespace WebBanHang.Models.ViewModel
{
    public class PriceHistoryViewModel
    {
        public int PriceHistoryID { get; set; }
        public int ProductID { get; set; }
        public string ProductName { get; set; }
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }
        public DateTime ChangeDate { get; set; }
    }
}