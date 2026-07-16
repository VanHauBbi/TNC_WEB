using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using WebBanHang.Models;

namespace WebBanHang.Services
{
    // Class DTO chứa thông tin ứng viên
    public class HybridRule
    {
        public int ProductID_A { get; set; }
        public int ProductID_B { get; set; }
        public decimal Confidence { get; set; }
        public int Support { get; set; }
        public decimal ActualUtility { get; set; }
        public decimal TWU { get; set; }
    }

    internal class TransactionNode
    {
        public int OrderID { get; set; }
        public Dictionary<int, decimal> ItemsUtility { get; set; }
        public decimal TransactionUtility { get; set; }
    }

    public class SmartRecommendationService
    {
        private MyStoreEntities db = new MyStoreEntities();

        /// <summary>
        /// Thuật toán Lai (Hybrid): Apriori + Two-Phase
        /// </summary>
        /// <param name="minConfidence">Độ tin cậy tối thiểu (20%)</param>
        /// <param name="minSupport">Tần suất mua chung tối thiểu (VD: 1 lần)</param>
        /// <param name="minUtilityThreshold">Lợi nhuận tối thiểu mang lại (100,000 VNĐ)</param>
        public void RunHybridAlgorithm(double minConfidence = 0.2, int minSupport = 1, decimal minUtilityThreshold = 100000m)
        {
            // 1. LẤY HÓA ĐƠN HỢP LỆ
            var rawOrders = db.OrderDetails
                .Where(od => od.Order.OrderStatus != "Đã hủy" && od.Order.PaymentStatus != "Thất bại")
                .GroupBy(od => od.OrderID)
                .ToList();

            if (!rawOrders.Any()) return;

            var transactions = new List<TransactionNode>();
            var itemFrequencies = new Dictionary<int, int>(); // Đếm Support của từng sản phẩm (Apriori)

            // 2. TÍNH TOÁN LỢI NHUẬN TỪNG HÓA ĐƠN VÀ ĐẾM TẦN SUẤT SẢN PHẨM
            foreach (var order in rawOrders)
            {
                var transNode = new TransactionNode
                {
                    OrderID = order.Key,
                    ItemsUtility = new Dictionary<int, decimal>(),
                    TransactionUtility = 0
                };

                var distinctItems = new HashSet<int>();

                foreach (var detail in order)
                {
                    decimal profitPerUnit = detail.UnitPrice - detail.ImportPrice;
                    if (profitPerUnit < 0) profitPerUnit = 0m;
                    decimal utility = profitPerUnit * detail.Quantity;

                    if (!transNode.ItemsUtility.ContainsKey(detail.ProductID))
                        transNode.ItemsUtility[detail.ProductID] = 0;

                    transNode.ItemsUtility[detail.ProductID] += utility;
                    transNode.TransactionUtility += utility;

                    distinctItems.Add(detail.ProductID);
                }

                foreach (var item in distinctItems)
                {
                    if (!itemFrequencies.ContainsKey(item))
                        itemFrequencies[item] = 0;
                    itemFrequencies[item]++;
                }

                transactions.Add(transNode);
            }

            // 3. PHASE 1: TÍNH TWU CHO SẢN PHẨM LẺ (1-ITEMSET)
            var twu1Itemsets = new Dictionary<int, decimal>();
            foreach (var trans in transactions)
            {
                foreach (var productId in trans.ItemsUtility.Keys)
                {
                    if (!twu1Itemsets.ContainsKey(productId))
                        twu1Itemsets[productId] = 0;
                    twu1Itemsets[productId] += trans.TransactionUtility;
                }
            }

            // Lọc sản phẩm lẻ: Đạt cả Tần Suất (Apriori) VÀ Lợi Nhuận Tiềm Năng (Two-Phase)
            var validItems = twu1Itemsets
                .Where(x => x.Value >= minUtilityThreshold && itemFrequencies[x.Key] >= minSupport)
                .Select(x => x.Key)
                .ToList();

            // 4. GHÉP CẶP VÀ TÍNH CHỈ SỐ CHO CẶP (2-ITEMSETS)
            var pairTwuFrequencies = new Dictionary<string, decimal>();
            var pairSupportCount = new Dictionary<string, int>();

            foreach (var trans in transactions)
            {
                var itemsInTrans = trans.ItemsUtility.Keys.Where(id => validItems.Contains(id)).ToList();
                for (int i = 0; i < itemsInTrans.Count; i++)
                {
                    for (int j = i + 1; j < itemsInTrans.Count; j++)
                    {
                        int itemA = itemsInTrans[i];
                        int itemB = itemsInTrans[j];
                        string pairKey = itemA < itemB ? $"{itemA}-{itemB}" : $"{itemB}-{itemA}";

                        if (!pairTwuFrequencies.ContainsKey(pairKey))
                        {
                            pairTwuFrequencies[pairKey] = 0;
                            pairSupportCount[pairKey] = 0;
                        }

                        pairTwuFrequencies[pairKey] += trans.TransactionUtility; // TWU
                        pairSupportCount[pairKey]++; // Support
                    }
                }
            }

            // 5. TÍNH CONFIDENCE (APRIORI) VÀ LỌC ỨNG VIÊN
            var candidates = new List<HybridRule>();
            foreach (var pair in pairTwuFrequencies)
            {
                if (pair.Value >= minUtilityThreshold && pairSupportCount[pair.Key] >= minSupport)
                {
                    var keys = pair.Key.Split('-');
                    int itemA = int.Parse(keys[0]);
                    int itemB = int.Parse(keys[1]);
                    int countAB = pairSupportCount[pair.Key];

                    double confA = (double)countAB / itemFrequencies[itemA]; // Mua A suy ra B
                    double confB = (double)countAB / itemFrequencies[itemB]; // Mua B suy ra A

                    if (confA >= minConfidence)
                    {
                        candidates.Add(new HybridRule { ProductID_A = itemA, ProductID_B = itemB, TWU = pair.Value, Support = countAB, Confidence = Math.Round((decimal)confA, 2) });
                    }
                    if (confB >= minConfidence)
                    {
                        candidates.Add(new HybridRule { ProductID_A = itemB, ProductID_B = itemA, TWU = pair.Value, Support = countAB, Confidence = Math.Round((decimal)confB, 2) });
                    }
                }
            }

            // 6. PHASE 2: TÍNH LỢI NHUẬN THỰC TẾ (ACTUAL UTILITY) CHO NHỮNG QUY TẮC ĐÃ LỌT LƯỚI
            var finalRules = new List<HybridRule>();
            foreach (var candidate in candidates)
            {
                decimal actualUtility = 0;
                foreach (var trans in transactions)
                {
                    if (trans.ItemsUtility.ContainsKey(candidate.ProductID_A) && trans.ItemsUtility.ContainsKey(candidate.ProductID_B))
                    {
                        actualUtility += trans.ItemsUtility[candidate.ProductID_A] + trans.ItemsUtility[candidate.ProductID_B];
                    }
                }

                // TÍNH LỢI NHUẬN TRUNG BÌNH CHO 1 LẦN MUA CHUNG (1 COMBO)
                decimal averageComboUtility = actualUtility / candidate.Support;

                // Lọc theo lợi nhuận của 1 Combo thay vì Lợi nhuận gộp
                if (averageComboUtility >= minUtilityThreshold)
                {
                    candidate.ActualUtility = averageComboUtility; // Ghi đè: Lưu lợi nhuận 1 đơn vào DB thay vì tổng gộp
                    finalRules.Add(candidate);
                }
            }

            // 7. LƯU DATABASE (UPSERT THÔNG MINH)
            var oldRules = db.SmartRecommendations.ToList();
            var rulesToRemove = oldRules.Where(o => !finalRules.Any(n => n.ProductID_A == o.ProductID_A && n.ProductID_B == o.ProductID_B)).ToList();
            if (rulesToRemove.Any()) db.SmartRecommendations.RemoveRange(rulesToRemove);

            foreach (var newRule in finalRules)
            {
                var existingRule = oldRules.FirstOrDefault(o => o.ProductID_A == newRule.ProductID_A && o.ProductID_B == newRule.ProductID_B);
                if (existingRule != null)
                {
                    if (existingRule.Confidence != newRule.Confidence || existingRule.Support != newRule.Support ||
                        existingRule.ActualUtility != newRule.ActualUtility || existingRule.TWU != newRule.TWU)
                    {
                        existingRule.Confidence = newRule.Confidence;
                        existingRule.Support = newRule.Support;
                        existingRule.ActualUtility = newRule.ActualUtility;
                        existingRule.TWU = newRule.TWU;
                        existingRule.UpdateDate = DateTime.Now;
                        db.Entry(existingRule).State = EntityState.Modified;
                    }
                }
                else
                {
                    db.SmartRecommendations.Add(new SmartRecommendation
                    {
                        ProductID_A = newRule.ProductID_A,
                        ProductID_B = newRule.ProductID_B,
                        Confidence = newRule.Confidence,
                        Support = newRule.Support,
                        ActualUtility = newRule.ActualUtility,
                        TWU = newRule.TWU,
                        UpdateDate = DateTime.Now
                    });
                }
            }
            db.SaveChanges();
        }
    }
}