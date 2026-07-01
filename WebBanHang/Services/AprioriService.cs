using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebBanHang.Models;

namespace WebBanHang.Services
{
    public class AprioriService
    {
        private MyStoreEntities db = new MyStoreEntities();

        /// <summary>
        /// Hàm chạy thuật toán Apriori
        /// </summary>
        /// <param name="minConfidence">Tỷ lệ tin cậy tối thiểu (Ví dụ: 0.2 = 20% khách mua A sẽ mua B)</param>
        public void RunAprioriAlgorithm(double minConfidence = 0.2)
        {
            // 1. Lấy tất cả các hóa đơn (Chỉ lấy các hóa đơn hợp lệ/đã giao nếu cần thiết)
            // Lấy danh sách ProductID không trùng lặp trong cùng 1 Order
            var transactions = db.OrderDetails
                .GroupBy(od => od.OrderID)
                .Select(g => g.Select(x => x.ProductID).Distinct().ToList())
                .ToList();

            int totalTransactions = transactions.Count;
            if (totalTransactions == 0) return;

            // 2. Đếm số lần xuất hiện của TỪNG sản phẩm (Support của A)
            var itemFrequencies = new Dictionary<int, int>();
            foreach (var t in transactions)
            {
                foreach (var item in t)
                {
                    if (!itemFrequencies.ContainsKey(item))
                        itemFrequencies[item] = 0;
                    itemFrequencies[item]++;
                }
            }

            // 3. Đếm số lần xuất hiện của các CẶP sản phẩm (Support của A giao B)
            var pairFrequencies = new Dictionary<string, int>();
            foreach (var t in transactions)
            {
                for (int i = 0; i < t.Count; i++)
                {
                    for (int j = 0; j < t.Count; j++)
                    {
                        if (i != j) // Mua A suy ra mua B (A -> B)
                        {
                            string pairKey = $"{t[i]}-{t[j]}"; // Format: "IdA-IdB"
                            if (!pairFrequencies.ContainsKey(pairKey))
                                pairFrequencies[pairKey] = 0;

                            pairFrequencies[pairKey]++;
                        }
                    }
                }
            }

            // 4. Xóa sạch dữ liệu gợi ý cũ trong Database để làm mới
            var oldRecords = db.ProductRecommendations.ToList();
            db.ProductRecommendations.RemoveRange(oldRecords);
            db.SaveChanges();

            // 5. Tính toán tỷ lệ Confidence và lưu vào DB những cặp đạt chuẩn
            var newRules = new List<ProductRecommendation>();
            foreach (var pair in pairFrequencies)
            {
                var keys = pair.Key.Split('-');
                int itemA = int.Parse(keys[0]);
                int itemB = int.Parse(keys[1]);
                int countAB = pair.Value; // Số lần mua chung A và B
                int countA = itemFrequencies[itemA]; // Số lần mua A

                // Tính Confidence: Tỷ lệ khách mua A sẽ tiếp tục mua B
                double confidence = (double)countAB / countA;

                if (confidence >= minConfidence)
                {
                    newRules.Add(new ProductRecommendation
                    {
                        ProductID_A = itemA,
                        ProductID_B = itemB,
                        Confidence = (decimal)confidence,
                        Support = countAB,
                        UpdateDate = DateTime.Now
                    });
                }
            }

            // Lưu toàn bộ luật mới vào CSDL
            if (newRules.Any())
            {
                db.ProductRecommendations.AddRange(newRules);
                db.SaveChanges();
            }
        }
    }
}
