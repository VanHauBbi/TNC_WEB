using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using WebBanHang.Models;
using WebBanHang.Models.ViewModel;
using CartViewItem = WebBanHang.Models.ViewModel.CartItem;

namespace WebBanHang.Services
{
    public class MarketingSellingService
    {
        private readonly MyStoreEntities db;

        public MarketingSellingService(MyStoreEntities context)
        {
            db = context ?? throw new ArgumentNullException(nameof(context));
        }

        public int GeneratePersonalVouchers(string createdBy, decimal minimumMarginPct = 5m)
        {
            EnsureSchema();
            ExpireOldOffers();
            var campaignId = GetOrCreateCampaign("Ưu đãi cá nhân tự động", "PERSONAL", minimumMarginPct, createdBy);

            var candidates = db.Database.SqlQuery<InterestCandidate>(@"
                SELECT l.CustomerID, l.ProductID,
                       CAST(SUM(l.ActionWeight * CASE
                           WHEN DATEDIFF(DAY, l.CreatedAt, GETDATE()) <= 1 THEN 1.00
                           WHEN DATEDIFF(DAY, l.CreatedAt, GETDATE()) <= 7 THEN 0.75
                           WHEN DATEDIFF(DAY, l.CreatedAt, GETDATE()) <= 14 THEN 0.50
                           ELSE 0.25 END) AS DECIMAL(10,2)) AS InterestScore,
                       CASE
                           WHEN MAX(CASE WHEN l.ActionType = 'REMOVE_CART' THEN 1 ELSE 0 END) = 1 THEN 'CART_ABANDONED'
                           WHEN MAX(CASE WHEN l.ActionType = 'ADD_CART' THEN 1 ELSE 0 END) = 1 THEN 'ADD_CART'
                           WHEN MAX(CASE WHEN l.ActionType = 'CLICK' THEN 1 ELSE 0 END) = 1 THEN 'CLICK'
                           ELSE 'VIEW' END AS TriggerType
                FROM dbo.UserBehaviorLogs l
                WHERE l.CustomerID IS NOT NULL
                  AND l.ProductID IS NOT NULL
                  AND l.CreatedAt >= DATEADD(DAY, -30, GETDATE())
                  AND l.ActionType IN ('VIEW','CLICK','DWELL_TIME','ADD_CART','REMOVE_CART')
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.UserBehaviorLogs bought
                      WHERE bought.CustomerID = l.CustomerID
                        AND bought.ProductID = l.ProductID
                        AND bought.ActionType IN ('BUY','PURCHASE')
                        AND bought.CreatedAt >= DATEADD(DAY, -30, GETDATE())
                  )
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.CustomerCoupon cc
                      WHERE cc.CustomerID = l.CustomerID
                        AND cc.TargetProductID = l.ProductID
                        AND cc.Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                        AND cc.ExpiresAt > SYSUTCDATETIME()
                  )
                GROUP BY l.CustomerID, l.ProductID
                HAVING SUM(l.ActionWeight * CASE
                           WHEN DATEDIFF(DAY, l.CreatedAt, GETDATE()) <= 1 THEN 1.00
                           WHEN DATEDIFF(DAY, l.CreatedAt, GETDATE()) <= 7 THEN 0.75
                           WHEN DATEDIFF(DAY, l.CreatedAt, GETDATE()) <= 14 THEN 0.50
                           ELSE 0.25 END) >= 8").ToList();

            var created = 0;
            foreach (var candidate in candidates)
            {
                var product = db.Products.SingleOrDefault(p => p.ProductID == candidate.ProductID && p.Status != 2 && p.StockQuantity > 0);
                if (product == null) continue;

                var marginBudget = product.ProductPrice - product.ImportPrice
                                 - (product.ProductPrice * minimumMarginPct / 100m);
                var proposed = Math.Min(product.ProductPrice * 0.05m, 150000m);
                var discount = RoundDownToThousand(Math.Min(proposed, marginBudget));
                if (discount < 10000m) continue;

                using (var transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        var code = "TNC-" + candidate.CustomerID + "-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
                        var expiresAt = DateTime.UtcNow.AddHours(72);
                        var couponId = Convert.ToInt32(db.Database.SqlQuery<decimal>(@"
                            INSERT dbo.Coupon
                                (CouponName, Code, DiscountPercentage, MaxDiscountAmount, ExpiryDate, UsageLimit,
                                 CouponType, DiscountType, FixedDiscountAmount, MinimumOrderValue, StartDate,
                                 IsActive, IsStackable, CampaignID, SourceType)
                            VALUES
                                (@name, @code, NULL, @discount, @expiry, 1,
                                 'PERSONAL', 'FIXED', @discount, 0, SYSUTCDATETIME(),
                                 1, 0, @campaignId, 'BEHAVIOR');
                            SELECT CAST(SCOPE_IDENTITY() AS DECIMAL(18,0));",
                            new SqlParameter("@name", "Ưu đãi riêng: " + product.ProductName),
                            new SqlParameter("@code", code),
                            new SqlParameter("@discount", discount),
                            new SqlParameter("@expiry", expiresAt),
                            new SqlParameter("@campaignId", campaignId)).Single());

                        db.Database.ExecuteSqlCommand(@"
                            INSERT dbo.CustomerCoupon
                                (CouponID, CustomerID, TargetProductID, TriggerType, InterestScore, Status, AssignedAt, ExpiresAt)
                            VALUES (@couponId, @customerId, @productId, @trigger, @score, 'ISSUED', SYSUTCDATETIME(), @expiry);",
                            new SqlParameter("@couponId", couponId),
                            new SqlParameter("@customerId", candidate.CustomerID),
                            new SqlParameter("@productId", candidate.ProductID),
                            new SqlParameter("@trigger", candidate.TriggerType),
                            new SqlParameter("@score", candidate.InterestScore),
                            new SqlParameter("@expiry", expiresAt));

                        transaction.Commit();
                        created++;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            return created;
        }

        public int GenerateComboOffers(string createdBy, decimal minimumMarginPct = 5m)
        {
            EnsureSchema();
            ExpireOldOffers();
            var campaignId = GetOrCreateCampaign("Combo mua chung tự động", "COMBO", minimumMarginPct, createdBy);
            var rules = db.SmartRecommendations
                .Where(r => r.Support >= 2 && r.Confidence >= 0.20m && r.ActualUtility >= 100000m)
                .OrderByDescending(r => r.ActualUtility)
                .ThenByDescending(r => r.Confidence)
                .Take(100)
                .ToList();

            var created = 0;
            foreach (var rule in rules)
            {
                var productA = db.Products.Find(rule.ProductID_A);
                var productB = db.Products.Find(rule.ProductID_B);
                if (productA == null || productB == null || productA.Status == 2 || productB.Status == 2
                    || productA.StockQuantity <= 0 || productB.StockQuantity <= 0) continue;

                var alreadyExists = db.Database.SqlQuery<int>(@"
                    SELECT COUNT(1) FROM dbo.ComboOffer
                    WHERE ProductID_A = @a AND ProductID_B = @b
                      AND IsActive = 1 AND EndDate > SYSUTCDATETIME();",
                    new SqlParameter("@a", rule.ProductID_A), new SqlParameter("@b", rule.ProductID_B)).Single() > 0;
                if (alreadyExists) continue;

                var revenue = productA.ProductPrice + productB.ProductPrice;
                var grossMargin = revenue - productA.ImportPrice - productB.ImportPrice;
                var marginBudget = grossMargin - revenue * minimumMarginPct / 100m;
                var proposed = Math.Min(Math.Min(revenue * 0.05m, rule.ActualUtility * 0.15m), 300000m);
                var discount = RoundDownToThousand(Math.Min(proposed, marginBudget));
                if (discount < 20000m) continue;

                var code = "COMBO-" + rule.ProductID_A + "-" + rule.ProductID_B + "-" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
                db.Database.ExecuteSqlCommand(@"
                    INSERT dbo.ComboOffer
                        (CampaignID, ProductID_A, ProductID_B, Code, DiscountAmount, Support,
                         Confidence, ActualUtility, MinimumMarginPct, StartDate, EndDate, UsageLimit, IsActive)
                    VALUES
                        (@campaignId, @a, @b, @code, @discount, @support,
                         @confidence, @utility, @margin, SYSUTCDATETIME(), DATEADD(DAY, 14, SYSUTCDATETIME()), 100, 1);",
                    new SqlParameter("@campaignId", campaignId),
                    new SqlParameter("@a", rule.ProductID_A),
                    new SqlParameter("@b", rule.ProductID_B),
                    new SqlParameter("@code", code),
                    new SqlParameter("@discount", discount),
                    new SqlParameter("@support", rule.Support),
                    new SqlParameter("@confidence", rule.Confidence),
                    new SqlParameter("@utility", rule.ActualUtility),
                    new SqlParameter("@margin", minimumMarginPct));
                created++;
            }
            return created;
        }

        public List<PersonalVoucherVM> GetCustomerVouchers(int customerId, bool markViewed = false)
        {
            if (!SchemaExists()) return new List<PersonalVoucherVM>();
            ExpireOldOffers();
            var vouchers = db.Database.SqlQuery<PersonalVoucherVM>(@"
                SELECT cc.CustomerCouponID, cc.CouponID, cc.TargetProductID, c.CouponName, c.Code,
                       p.ProductName, p.ProductImage,
                       CAST(COALESCE(c.FixedDiscountAmount, c.MaxDiscountAmount, 0) AS DECIMAL(18,3)) DiscountAmount,
                       cc.InterestScore, cc.TriggerType, cc.Status, CAST(cc.ExpiresAt AS DATETIME) ExpiresAt
                FROM dbo.CustomerCoupon cc
                INNER JOIN dbo.Coupon c ON c.CouponID = cc.CouponID
                LEFT JOIN dbo.Product p ON p.ProductID = cc.TargetProductID
                WHERE cc.CustomerID = @customerId
                  AND cc.Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                  AND cc.ExpiresAt > SYSUTCDATETIME()
                  AND c.IsActive = 1
                ORDER BY cc.InterestScore DESC, cc.ExpiresAt ASC;",
                new SqlParameter("@customerId", customerId)).ToList();

            if (markViewed && vouchers.Any())
            {
                db.Database.ExecuteSqlCommand(@"
                    UPDATE dbo.CustomerCoupon
                    SET ViewedAt = COALESCE(ViewedAt, SYSUTCDATETIME()),
                        Status = CASE WHEN Status = 'ISSUED' THEN 'VIEWED' ELSE Status END
                    WHERE CustomerID = @customerId AND Status = 'ISSUED' AND ExpiresAt > SYSUTCDATETIME();",
                    new SqlParameter("@customerId", customerId));
            }
            return vouchers;
        }

        public List<ComboOfferVM> GetComboOffers(IEnumerable<int> productIds = null)
        {
            if (!SchemaExists()) return new List<ComboOfferVM>();
            var ids = (productIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            var sql = @"
                SELECT co.ComboOfferID, co.ProductID_A, co.ProductID_B,
                       a.ProductName ProductNameA, b.ProductName ProductNameB, b.ProductImage ProductImageB,
                       co.Code, co.DiscountAmount, co.Support, co.Confidence, co.ActualUtility,
                       CAST(co.EndDate AS DATETIME) EndDate
                FROM dbo.ComboOffer co
                INNER JOIN dbo.Product a ON a.ProductID = co.ProductID_A
                INNER JOIN dbo.Product b ON b.ProductID = co.ProductID_B
                WHERE co.IsActive = 1 AND co.UsageLimit > 0
                  AND co.StartDate <= SYSUTCDATETIME() AND co.EndDate > SYSUTCDATETIME()";

            var parameters = new List<SqlParameter>();
            if (ids.Any())
            {
                var names = new List<string>();
                for (var i = 0; i < ids.Count; i++)
                {
                    var name = "@p" + i;
                    names.Add(name);
                    parameters.Add(new SqlParameter(name, ids[i]));
                }
                sql += " AND co.ProductID_A IN (" + string.Join(",", names) + ")";
            }
            sql += " ORDER BY co.ActualUtility DESC, co.Confidence DESC";
            return db.Database.SqlQuery<ComboOfferVM>(sql, parameters.Cast<object>().ToArray()).Take(12).ToList();
        }

        public PromotionEvaluation EvaluatePromotion(string code, int customerId, IEnumerable<CartViewItem> items)
        {
            var cartItems = (items ?? Enumerable.Empty<CartViewItem>()).ToList();
            if (string.IsNullOrWhiteSpace(code) || !cartItems.Any() || !SchemaExists())
                return Invalid("Mã ưu đãi không hợp lệ.");

            code = code.Trim().ToUpperInvariant();
            var personal = db.Database.SqlQuery<PersonalPromotionRow>(@"
                SELECT TOP 1 cc.CustomerCouponID, cc.CouponID, cc.TargetProductID,
                       c.Code, COALESCE(c.FixedDiscountAmount, c.MaxDiscountAmount, 0) DiscountAmount
                FROM dbo.CustomerCoupon cc
                INNER JOIN dbo.Coupon c ON c.CouponID = cc.CouponID
                WHERE cc.CustomerID = @customerId AND c.Code = @code
                  AND cc.Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                  AND cc.ExpiresAt > SYSUTCDATETIME()
                  AND c.IsActive = 1 AND c.UsageLimit > 0;",
                new SqlParameter("@customerId", customerId), new SqlParameter("@code", code)).FirstOrDefault();

            if (personal != null)
            {
                if (!personal.TargetProductID.HasValue || cartItems.All(x => x.ProductID != personal.TargetProductID.Value))
                    return Invalid("Voucher này chỉ áp dụng cho sản phẩm được chỉ định.");

                var allowed = ApplyMarginGuard(personal.DiscountAmount, cartItems);
                if (allowed <= 0) return Invalid("Ưu đãi không thể áp dụng vì không đạt biên lợi nhuận tối thiểu.");
                return new PromotionEvaluation
                {
                    IsValid = true, Message = "Áp dụng voucher cá nhân thành công.", PromotionType = "PERSONAL",
                    Code = code, DiscountAmount = allowed, CouponID = personal.CouponID,
                    CustomerCouponID = personal.CustomerCouponID
                };
            }

            var combo = db.Database.SqlQuery<ComboPromotionRow>(@"
                SELECT TOP 1 ComboOfferID, ProductID_A, ProductID_B, Code, DiscountAmount
                FROM dbo.ComboOffer
                WHERE Code = @code AND IsActive = 1 AND UsageLimit > 0
                  AND StartDate <= SYSUTCDATETIME() AND EndDate > SYSUTCDATETIME();",
                new SqlParameter("@code", code)).FirstOrDefault();
            if (combo == null) return Invalid("Mã ưu đãi không tồn tại hoặc đã hết hạn.");
            if (cartItems.All(x => x.ProductID != combo.ProductID_A) || cartItems.All(x => x.ProductID != combo.ProductID_B))
                return Invalid("Bạn cần có đủ hai sản phẩm của combo trong giỏ hàng.");

            var comboAllowed = ApplyMarginGuard(combo.DiscountAmount, cartItems);
            if (comboAllowed <= 0) return Invalid("Combo không thể áp dụng vì không đạt biên lợi nhuận tối thiểu.");
            return new PromotionEvaluation
            {
                IsValid = true, Message = "Áp dụng ưu đãi combo thành công.", PromotionType = "COMBO",
                Code = code, DiscountAmount = comboAllowed, ComboOfferID = combo.ComboOfferID
            };
        }

        public bool IsManagedPromotionCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || !SchemaExists()) return false;
            return db.Database.SqlQuery<int>(@"
                SELECT CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.Coupon WHERE Code = @code AND CouponType = 'PERSONAL'
                    UNION ALL
                    SELECT 1 FROM dbo.ComboOffer WHERE Code = @code
                ) THEN 1 ELSE 0 END;", new SqlParameter("@code", code.Trim().ToUpperInvariant())).Single() == 1;
        }

        public List<Coupon> GetPublicCoupons()
        {
            if (!SchemaExists())
                return db.Coupons.Where(c => !c.Products.Any()).OrderByDescending(c => c.CouponID).ToList();

            var ids = db.Database.SqlQuery<int>(@"
                SELECT CouponID FROM dbo.Coupon
                WHERE CouponType = 'GLOBAL' AND IsActive = 1
                  AND (StartDate IS NULL OR StartDate <= SYSUTCDATETIME())
                  AND ExpiryDate > GETDATE() AND UsageLimit > 0;").ToList();
            return db.Coupons.Where(c => ids.Contains(c.CouponID) && !c.Products.Any())
                .OrderByDescending(c => c.CouponID).ToList();
        }

        public bool RecordPromotion(int orderId, PromotionEvaluation promotion)
        {
            if (promotion == null || !promotion.IsValid || !SchemaExists()) return false;
            db.Database.ExecuteSqlCommand(@"
                INSERT dbo.OrderPromotion
                    (OrderID, PromotionType, CouponID, CustomerCouponID, ComboOfferID, PromotionCode, DiscountAmount)
                VALUES (@orderId, @type, @couponId, @customerCouponId, @comboId, @code, @discount);

                UPDATE dbo.CustomerCoupon
                SET Status = 'USED', UsedAt = SYSUTCDATETIME(), OrderID = @orderId
                WHERE CustomerCouponID = @customerCouponId;

                UPDATE dbo.Coupon SET UsageLimit = CASE WHEN UsageLimit > 0 THEN UsageLimit - 1 ELSE 0 END
                WHERE CouponID = @couponId;

                UPDATE dbo.ComboOffer SET UsageLimit = CASE WHEN UsageLimit > 0 THEN UsageLimit - 1 ELSE 0 END
                WHERE ComboOfferID = @comboId;",
                new SqlParameter("@orderId", orderId),
                new SqlParameter("@type", promotion.PromotionType),
                new SqlParameter("@couponId", SqlDbType.Int) { Value = (object)promotion.CouponID ?? DBNull.Value },
                new SqlParameter("@customerCouponId", SqlDbType.Int) { Value = (object)promotion.CustomerCouponID ?? DBNull.Value },
                new SqlParameter("@comboId", SqlDbType.Int) { Value = (object)promotion.ComboOfferID ?? DBNull.Value },
                new SqlParameter("@code", promotion.Code),
                new SqlParameter("@discount", promotion.DiscountAmount));
            return true;
        }

        public void RollbackPromotions(int orderId)
        {
            if (!SchemaExists()) return;
            db.Database.ExecuteSqlCommand(@"
                UPDATE c SET c.UsageLimit = c.UsageLimit + 1
                FROM dbo.Coupon c
                INNER JOIN dbo.OrderPromotion op ON op.CouponID = c.CouponID
                WHERE op.OrderID = @orderId;

                UPDATE co SET co.UsageLimit = co.UsageLimit + 1
                FROM dbo.ComboOffer co
                INNER JOIN dbo.OrderPromotion op ON op.ComboOfferID = co.ComboOfferID
                WHERE op.OrderID = @orderId;

                UPDATE cc
                SET cc.Status = 'CLICKED', cc.UsedAt = NULL, cc.OrderID = NULL
                FROM dbo.CustomerCoupon cc
                INNER JOIN dbo.OrderPromotion op ON op.CustomerCouponID = cc.CustomerCouponID
                WHERE op.OrderID = @orderId;

                DELETE FROM dbo.OrderPromotion WHERE OrderID = @orderId;",
                new SqlParameter("@orderId", orderId));
        }

        public void TrackVoucherClick(int customerId, int customerCouponId)
        {
            if (!SchemaExists()) return;
            db.Database.ExecuteSqlCommand(@"
                UPDATE dbo.CustomerCoupon
                SET ClickedAt = COALESCE(ClickedAt, SYSUTCDATETIME()),
                    Status = CASE WHEN Status IN ('ISSUED','VIEWED') THEN 'CLICKED' ELSE Status END
                WHERE CustomerCouponID = @id AND CustomerID = @customerId
                  AND Status IN ('ISSUED','VIEWED','CLICKED','CARTED');",
                new SqlParameter("@id", customerCouponId), new SqlParameter("@customerId", customerId));
        }

        public void TrackCartedByProduct(int customerId, int productId)
        {
            if (!SchemaExists()) return;
            db.Database.ExecuteSqlCommand(@"
                UPDATE dbo.CustomerCoupon
                SET AddedToCartAt = COALESCE(AddedToCartAt, SYSUTCDATETIME()), Status = 'CARTED'
                WHERE CustomerID = @customerId AND TargetProductID = @productId
                  AND Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                  AND ExpiresAt > SYSUTCDATETIME();",
                new SqlParameter("@customerId", customerId), new SqlParameter("@productId", productId));
        }

        public MarketingDashboardVM GetDashboard()
        {
            var vm = new MarketingDashboardVM();
            if (!SchemaExists()) return vm;
            ExpireOldOffers();
            vm.ActivePersonalVouchers = db.Database.SqlQuery<int>("SELECT COUNT(1) FROM dbo.CustomerCoupon WHERE Status IN ('ISSUED','VIEWED','CLICKED','CARTED') AND ExpiresAt > SYSUTCDATETIME()").Single();
            vm.ActiveComboOffers = db.Database.SqlQuery<int>("SELECT COUNT(1) FROM dbo.ComboOffer WHERE IsActive = 1 AND EndDate > SYSUTCDATETIME() AND UsageLimit > 0").Single();
            vm.RedeemedVouchers = db.Database.SqlQuery<int>("SELECT COUNT(1) FROM dbo.CustomerCoupon WHERE Status = 'USED'").Single();
            vm.RevenueInfluenced = db.Database.SqlQuery<decimal>("SELECT COALESCE(SUM(o.TotalAmount), 0) FROM dbo.OrderPromotion op INNER JOIN dbo.[Order] o ON o.OrderID = op.OrderID").Single();
            vm.RecentPersonalVouchers = db.Database.SqlQuery<PersonalVoucherVM>(@"
                SELECT TOP 20 cc.CustomerCouponID, cc.CouponID, cc.TargetProductID, c.CouponName, c.Code,
                       p.ProductName, p.ProductImage,
                       CAST(COALESCE(c.FixedDiscountAmount, c.MaxDiscountAmount, 0) AS DECIMAL(18,3)) DiscountAmount,
                       cc.InterestScore, cc.TriggerType, cc.Status, CAST(cc.ExpiresAt AS DATETIME) ExpiresAt
                FROM dbo.CustomerCoupon cc INNER JOIN dbo.Coupon c ON c.CouponID = cc.CouponID
                LEFT JOIN dbo.Product p ON p.ProductID = cc.TargetProductID
                ORDER BY cc.AssignedAt DESC").ToList();
            vm.ActiveCombos = GetComboOffers();
            return vm;
        }

        private decimal ApplyMarginGuard(decimal requestedDiscount, List<CartViewItem> cartItems)
        {
            decimal revenue = 0m;
            decimal fifoCost = 0m;
            foreach (var item in cartItems)
            {
                revenue += item.TotalPrice;
                var needed = item.Quantity;
                var batches = db.ImportReceiptDetails
                    .Where(x => x.ProductID == item.ProductID && x.RemainingQuantity > 0)
                    .OrderBy(x => x.DetailID).ToList();
                foreach (var batch in batches)
                {
                    if (needed <= 0) break;
                    var take = Math.Min(needed, batch.RemainingQuantity);
                    fifoCost += take * batch.ImportPrice;
                    needed -= take;
                }
                if (needed > 0)
                {
                    var product = db.Products.Find(item.ProductID);
                    fifoCost += needed * (product?.ImportPrice ?? item.OriginalPrice);
                }
            }

            var maximumDiscount = revenue - fifoCost - revenue * 0.05m;
            return RoundDownToThousand(Math.Max(0m, Math.Min(requestedDiscount, maximumDiscount)));
        }

        private int GetOrCreateCampaign(string name, string type, decimal margin, string createdBy)
        {
            var existing = db.Database.SqlQuery<int>(@"
                SELECT TOP 1 CampaignID FROM dbo.MarketingCampaign
                WHERE CampaignName = @name AND CampaignType = @type AND IsActive = 1 AND EndDate > SYSUTCDATETIME()
                ORDER BY CampaignID DESC;",
                new SqlParameter("@name", name), new SqlParameter("@type", type)).FirstOrDefault();
            if (existing > 0) return existing;

            return Convert.ToInt32(db.Database.SqlQuery<decimal>(@"
                INSERT dbo.MarketingCampaign
                    (CampaignName, CampaignType, SourceType, StartDate, EndDate, MinimumMarginPct, IsActive, CreatedBy)
                VALUES (@name, @type, 'HYBRID', SYSUTCDATETIME(), DATEADD(DAY, 30, SYSUTCDATETIME()), @margin, 1, @createdBy);
                SELECT CAST(SCOPE_IDENTITY() AS DECIMAL(18,0));",
                new SqlParameter("@name", name), new SqlParameter("@type", type),
                new SqlParameter("@margin", margin), new SqlParameter("@createdBy", (object)createdBy ?? DBNull.Value)).Single());
        }

        private void ExpireOldOffers()
        {
            db.Database.ExecuteSqlCommand(@"
                UPDATE dbo.CustomerCoupon SET Status = 'EXPIRED'
                WHERE Status IN ('ISSUED','VIEWED','CLICKED','CARTED') AND ExpiresAt <= SYSUTCDATETIME();
                UPDATE dbo.Coupon SET IsActive = 0
                WHERE CouponType = 'PERSONAL' AND ExpiryDate <= GETDATE();
                UPDATE dbo.ComboOffer SET IsActive = 0 WHERE EndDate <= SYSUTCDATETIME();");
        }

        private bool SchemaExists()
        {
            return db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID('dbo.CustomerCoupon','U') IS NOT NULL AND OBJECT_ID('dbo.ComboOffer','U') IS NOT NULL THEN 1 ELSE 0 END").Single() == 1;
        }

        private void EnsureSchema()
        {
            if (!SchemaExists()) throw new InvalidOperationException("Chưa cài đặt schema Marketing Selling trong database.");
        }

        private static decimal RoundDownToThousand(decimal value) => Math.Floor(value / 1000m) * 1000m;
        private static PromotionEvaluation Invalid(string message) => new PromotionEvaluation { IsValid = false, Message = message };

        private class InterestCandidate
        {
            public int CustomerID { get; set; }
            public int ProductID { get; set; }
            public decimal InterestScore { get; set; }
            public string TriggerType { get; set; }
        }

        private class PersonalPromotionRow
        {
            public int CustomerCouponID { get; set; }
            public int CouponID { get; set; }
            public int? TargetProductID { get; set; }
            public string Code { get; set; }
            public decimal DiscountAmount { get; set; }
        }

        private class ComboPromotionRow
        {
            public int ComboOfferID { get; set; }
            public int ProductID_A { get; set; }
            public int ProductID_B { get; set; }
            public string Code { get; set; }
            public decimal DiscountAmount { get; set; }
        }
    }
}
