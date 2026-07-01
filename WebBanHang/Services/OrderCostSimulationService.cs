using System;
using System.Collections.Generic;
using System.Linq;
using WebBanHang.Models;

namespace WebBanHang.Services
{
    public class MarginViolation
    {
        public int ProductID { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalCOGS { get; set; }
        public decimal MarginPercentage { get; set; }
    }

    public class SimulationResult
    {
        public decimal TotalRevenue { get; set; }
        public decimal TotalCOGS { get; set; }
        public bool IsViolatingMargin { get; set; }
        public List<MarginViolation> Violations { get; set; } = new List<MarginViolation>();
    }

    public class OrderCostSimulationService
    {
        private const decimal MaxLossPercentage = 0.05m;

        public SimulationResult SimulateCartCost(WebBanHang.Models.ViewModel.Cart cart, MyStoreEntities db)
        {
            var result = new SimulationResult();

            foreach (var item in cart.Items)
            {
                int quantityNeeded = item.Quantity;
                decimal itemRevenue = quantityNeeded * item.UnitPrice;
                result.TotalRevenue += itemRevenue;

                var availableBatches = db.ImportReceiptDetails
                                         .Where(b => b.ProductID == item.ProductID && b.RemainingQuantity > 0)
                                         .OrderBy(b => b.DetailID)
                                         .ToList();

                decimal itemTotalCOGS = 0;
                int tempQtyNeeded = quantityNeeded;

                foreach (var batch in availableBatches)
                {
                    if (tempQtyNeeded <= 0) break;
                    int qtyToTake = Math.Min(tempQtyNeeded, batch.RemainingQuantity);
                    itemTotalCOGS += qtyToTake * batch.ImportPrice;
                    tempQtyNeeded -= qtyToTake;
                }

                if (tempQtyNeeded > 0)
                {
                    var product = db.Products.Find(item.ProductID);
                    decimal fallbackCost = product != null ? product.ImportPrice : 0;
                    itemTotalCOGS += tempQtyNeeded * fallbackCost;
                }

                result.TotalCOGS += itemTotalCOGS;

                decimal itemMargin = 0;
                if (itemRevenue > 0)
                {
                    itemMargin = (itemRevenue - itemTotalCOGS) / itemRevenue;
                }

                // Ghi nhận sản phẩm vi phạm
                if (itemMargin < -MaxLossPercentage)
                {
                    result.IsViolatingMargin = true;
                    result.Violations.Add(new MarginViolation
                    {
                        ProductID = item.ProductID,
                        TotalRevenue = itemRevenue,
                        TotalCOGS = itemTotalCOGS,
                        MarginPercentage = itemMargin * 100 // Đổi sang định dạng phần trăm (%)
                    });
                }
            }

            return result;
        }
    }
}