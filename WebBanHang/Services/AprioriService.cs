using System;
using System.Collections.Generic;
using System.Data.Entity;
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
        /// Hàm chạy thuật toán Apriori (Phiên bản Cập nhật thông minh)
        /// </summary>
        public void RunAprioriAlgorithm(double minConfidence = 0.2)
        {
            // 1. LẤY TẤT CẢ HÓA ĐƠN HỢP LỆ
            // Đã BỔ SUNG: Loại bỏ những đơn "Đã hủy" hoặc "Thất bại" để AI không học kiến thức rác
            var transactions = db.OrderDetails
                .Where(od => od.Order.OrderStatus != "Đã hủy" && od.Order.PaymentStatus != "Thất bại")
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
                            string pairKey = $"{t[i]}-{t[j]}";
                            if (!pairFrequencies.ContainsKey(pairKey))
                                pairFrequencies[pairKey] = 0;

                            pairFrequencies[pairKey]++;
                        }
                    }
                }
            }

            // 4. Tính toán tỷ lệ Confidence và giữ lại những cặp đạt chuẩn
            var newRulesCalculated = new List<ProductRecommendation>();
            foreach (var pair in pairFrequencies)
            {
                var keys = pair.Key.Split('-');
                int itemA = int.Parse(keys[0]);
                int itemB = int.Parse(keys[1]);
                int countAB = pair.Value;
                int countA = itemFrequencies[itemA];

                double rawConfidence = (double)countAB / countA;
                decimal roundedConfidence = Math.Round((decimal)rawConfidence, 2);

                if (rawConfidence >= minConfidence)
                {
                    newRulesCalculated.Add(new ProductRecommendation
                    {
                        ProductID_A = itemA,
                        ProductID_B = itemB,
                        Confidence = roundedConfidence,
                        Support = countAB,
                        UpdateDate = DateTime.Now
                    });
                }
            }

            // ==========================================================
            // 5. LOGIC UPSERT: CHỈ CẬP NHẬT GIỜ KHI CÓ SỰ THAY ĐỔI
            // ==========================================================

            // Lấy danh sách các luật hiện đang có trong Database
            var oldRules = db.ProductRecommendations.ToList();

            // A. Xóa những luật cũ không còn đạt chuẩn
            var rulesToRemove = oldRules.Where(o => !newRulesCalculated.Any(n => n.ProductID_A == o.ProductID_A && n.ProductID_B == o.ProductID_B)).ToList();
            if (rulesToRemove.Any())
            {
                db.ProductRecommendations.RemoveRange(rulesToRemove);
            }

            // B. Thêm mới hoặc Cập nhật luật hiện có
            foreach (var newRule in newRulesCalculated)
            {
                var existingRule = oldRules.FirstOrDefault(o => o.ProductID_A == newRule.ProductID_A && o.ProductID_B == newRule.ProductID_B);

                if (existingRule != null)
                {
                    // Đảm bảo chỉ Update giờ nếu Độ tin cậy hoặc Tần suất có thay đổi
                    if (existingRule.Confidence != newRule.Confidence || existingRule.Support != newRule.Support)
                    {
                        existingRule.Confidence = newRule.Confidence;
                        existingRule.Support = newRule.Support;
                        existingRule.UpdateDate = DateTime.Now;

                        db.Entry(existingRule).State = EntityState.Modified;
                    }
                }
                else
                {
                    // Thêm mới
                    db.ProductRecommendations.Add(newRule);
                }
            }
            db.SaveChanges();
        }
    }
}