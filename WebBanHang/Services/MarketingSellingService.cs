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

        public int GeneratePersonalVouchers(string createdBy)
        {
            EnsureSchema();
            ExpireOldOffers();
            var settings = GetOrCreateCampaignSettings("PERSONAL", createdBy);

            // Mỗi hành vi chỉ được tính trong giới hạn/ngày, sau đó chỉ lấy sản phẩm
            // có điểm cao nhất của từng khách. Một khách đang có voucher sẽ không được phát thêm.
            var candidates = db.Database.SqlQuery<InterestCandidate>(@"
                WITH DailyRanked AS
                (
                    SELECT l.CustomerID, l.ProductID, l.ActionType, l.CreatedAt,
                           ROW_NUMBER() OVER
                           (
                               PARTITION BY l.CustomerID, l.ProductID, l.ActionType, CONVERT(date, l.CreatedAt)
                               ORDER BY l.CreatedAt DESC, l.LogID DESC
                           ) AS DailyRank
                    FROM dbo.UserBehaviorLogs l
                    WHERE l.CustomerID IS NOT NULL
                      AND l.ProductID IS NOT NULL
                      AND l.CreatedAt >= DATEADD(DAY, -30, GETDATE())
                      AND l.ActionType IN ('VIEW','CLICK','DWELL_TIME','ADD_CART','REMOVE_CART')
                ), Scored AS
                (
                    SELECT CustomerID, ProductID, ActionType,
                           CAST((CASE ActionType
                               WHEN 'VIEW' THEN 1 WHEN 'CLICK' THEN 2 WHEN 'DWELL_TIME' THEN 2
                               WHEN 'ADD_CART' THEN 5 WHEN 'REMOVE_CART' THEN 4 ELSE 0 END)
                           * (CASE
                               WHEN DATEDIFF(DAY, CreatedAt, GETDATE()) <= 1 THEN 1.00
                               WHEN DATEDIFF(DAY, CreatedAt, GETDATE()) <= 7 THEN 0.75
                               WHEN DATEDIFF(DAY, CreatedAt, GETDATE()) <= 14 THEN 0.50
                               ELSE 0.25 END) AS DECIMAL(10,2)) AS Score
                    FROM DailyRanked
                    WHERE DailyRank <= CASE WHEN ActionType IN ('VIEW','CLICK') THEN 2 ELSE 1 END
                ), Aggregated AS
                (
                    SELECT s.CustomerID, s.ProductID, SUM(s.Score) AS InterestScore,
                           CASE
                               WHEN MAX(CASE WHEN s.ActionType = 'REMOVE_CART' THEN 1 ELSE 0 END) = 1 THEN 'CART_ABANDONED'
                               WHEN MAX(CASE WHEN s.ActionType = 'ADD_CART' THEN 1 ELSE 0 END) = 1 THEN 'ADD_CART'
                               WHEN MAX(CASE WHEN s.ActionType = 'CLICK' THEN 1 ELSE 0 END) = 1 THEN 'CLICK'
                               ELSE 'VIEW' END AS TriggerType
                    FROM Scored s
                    WHERE NOT EXISTS
                    (
                        SELECT 1 FROM dbo.UserBehaviorLogs bought
                        WHERE bought.CustomerID = s.CustomerID AND bought.ProductID = s.ProductID
                          AND bought.ActionType IN ('BUY','PURCHASE')
                          AND bought.CreatedAt >= DATEADD(DAY, -30, GETDATE())
                    )
                    GROUP BY s.CustomerID, s.ProductID
                    HAVING SUM(s.Score) >= @minScore
                ), RankedCandidates AS
                (
                    SELECT a.*,
                           ROW_NUMBER() OVER
                           (
                               PARTITION BY a.CustomerID
                               ORDER BY a.InterestScore DESC,
                                        CASE a.TriggerType WHEN 'CART_ABANDONED' THEN 4 WHEN 'ADD_CART' THEN 3 WHEN 'CLICK' THEN 2 ELSE 1 END DESC,
                                        a.ProductID DESC
                           ) AS CandidateRank
                    FROM Aggregated a
                    WHERE NOT EXISTS
                    (
                        SELECT 1 FROM dbo.CustomerCoupon activeVoucher
                        WHERE activeVoucher.CustomerID = a.CustomerID
                          AND activeVoucher.Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                          AND activeVoucher.ExpiresAt > SYSUTCDATETIME()
                    )
                      AND NOT EXISTS
                    (
                        SELECT 1 FROM dbo.CustomerCoupon previousVoucher
                        WHERE previousVoucher.CustomerID = a.CustomerID
                          AND previousVoucher.TargetProductID = a.ProductID
                          AND COALESCE(previousVoucher.UsedAt, previousVoucher.ExpiresAt)
                              > DATEADD(DAY, -@cooldownDays, SYSUTCDATETIME())
                    )
                )
                SELECT CustomerID, ProductID, CAST(InterestScore AS DECIMAL(10,2)) InterestScore, TriggerType
                FROM RankedCandidates WHERE CandidateRank = 1;",
                new SqlParameter("@minScore", settings.MinInterestScore),
                new SqlParameter("@cooldownDays", settings.CooldownDays)).ToList();

            var created = 0;
            foreach (var candidate in candidates)
            {
                var product = db.Products.SingleOrDefault(p => p.ProductID == candidate.ProductID && p.Status != 2 && p.StockQuantity > 0);
                if (product == null) continue;

                var marginBudget = product.ProductPrice - product.ImportPrice
                                 - (product.ProductPrice * settings.MinimumMarginPct / 100m);
                var proposed = Math.Min(product.ProductPrice * settings.DiscountPercentage / 100m,
                                        settings.MaxDiscountAmount);
                var discount = RoundDownToThousand(Math.Min(proposed, marginBudget));
                if (discount <= 0m) continue;

                using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        var activeCount = db.Database.SqlQuery<int>(@"
                            SELECT COUNT(1) FROM dbo.CustomerCoupon WITH (UPDLOCK, HOLDLOCK)
                            WHERE CustomerID = @customerId
                              AND Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                              AND ExpiresAt > SYSUTCDATETIME();",
                            new SqlParameter("@customerId", candidate.CustomerID)).Single();
                        if (activeCount > 0)
                        {
                            transaction.Rollback();
                            continue;
                        }

                        var code = "TNC-" + candidate.CustomerID + "-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
                        var expiresAt = DateTime.UtcNow.AddHours(settings.ValidityHours);
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
                            new SqlParameter("@campaignId", settings.CampaignID)).Single());

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

        public int GenerateComboOffers(string createdBy)
        {
            EnsureSchema();
            ExpireOldOffers();
            var settings = GetOrCreateCampaignSettings("COMBO", createdBy);
            var rules = db.SmartRecommendations
                .Where(r => r.Support >= settings.MinSupport
                         && r.Confidence >= settings.MinConfidence
                         && r.ActualUtility >= settings.MinUtility)
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
                    WHERE ((ProductID_A = @a AND ProductID_B = @b)
                        OR (ProductID_A = @b AND ProductID_B = @a))
                      AND IsActive = 1 AND EndDate > SYSUTCDATETIME();",
                    new SqlParameter("@a", rule.ProductID_A), new SqlParameter("@b", rule.ProductID_B)).Single() > 0;
                if (alreadyExists) continue;

                var revenue = productA.ProductPrice + productB.ProductPrice;
                var grossMargin = revenue - productA.ImportPrice - productB.ImportPrice;
                var marginBudget = grossMargin - revenue * settings.MinimumMarginPct / 100m;
                var proposed = Math.Min(revenue * settings.DiscountPercentage / 100m,
                                        settings.MaxDiscountAmount);
                var discount = RoundDownToThousand(Math.Min(proposed, marginBudget));
                if (discount <= 0m) continue;

                var code = "COMBO-" + rule.ProductID_A + "-" + rule.ProductID_B + "-" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
                db.Database.ExecuteSqlCommand(@"
                    INSERT dbo.ComboOffer
                        (CampaignID, ProductID_A, ProductID_B, Code, DiscountAmount, Support,
                         Confidence, ActualUtility, MinimumMarginPct, StartDate, EndDate, UsageLimit, IsActive)
                    VALUES
                        (@campaignId, @a, @b, @code, @discount, @support,
                         @confidence, @utility, @margin, SYSUTCDATETIME(), DATEADD(DAY, @validityDays, SYSUTCDATETIME()), @usageLimit, 1);",
                    new SqlParameter("@campaignId", settings.CampaignID),
                    new SqlParameter("@a", rule.ProductID_A),
                    new SqlParameter("@b", rule.ProductID_B),
                    new SqlParameter("@code", code),
                    new SqlParameter("@discount", discount),
                    new SqlParameter("@support", rule.Support),
                    new SqlParameter("@confidence", rule.Confidence),
                    new SqlParameter("@utility", rule.ActualUtility),
                    new SqlParameter("@margin", settings.MinimumMarginPct),
                    new SqlParameter("@validityDays", settings.ValidityDays),
                    new SqlParameter("@usageLimit", settings.UsageLimit));
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
                       a.ProductName ProductNameA, b.ProductName ProductNameB,
                       a.ProductImage ProductImageA, b.ProductImage ProductImageB,
                       a.ProductPrice ProductPriceA, b.ProductPrice ProductPriceB,
                       co.Code, co.DiscountAmount, co.Support, co.Confidence, co.ActualUtility, co.MinimumMarginPct,
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
                var inClause = string.Join(",", names);
                sql += ids.Count == 1
                    ? " AND (co.ProductID_A IN (" + inClause + ") OR co.ProductID_B IN (" + inClause + "))"
                    : " AND co.ProductID_A IN (" + inClause + ") AND co.ProductID_B IN (" + inClause + ")";
            }
            sql += " ORDER BY co.DiscountAmount DESC, co.ActualUtility DESC, co.Confidence DESC";
            return db.Database.SqlQuery<ComboOfferVM>(sql, parameters.Cast<object>().ToArray()).Take(12).ToList();
        }

        public PromotionEvaluation GetBestCombo(IEnumerable<CartViewItem> items)
        {
            var cartItems = (items ?? Enumerable.Empty<CartViewItem>()).Where(x => x.Quantity > 0).ToList();
            if (!cartItems.Any() || !SchemaExists()) return Invalid("Giỏ hàng chưa đủ điều kiện combo.");

            PromotionEvaluation best = null;
            foreach (var combo in GetComboOffers(cartItems.Select(x => x.ProductID)))
            {
                var pair = cartItems.Where(x => x.ProductID == combo.ProductID_A || x.ProductID == combo.ProductID_B).ToList();
                if (pair.Select(x => x.ProductID).Distinct().Count() != 2) continue;

                var allowed = ApplyMarginGuard(combo.DiscountAmount, pair, combo.MinimumMarginPct, true);
                if (allowed <= 0m) continue;

                var currentPairAmount = pair.Sum(x => x.UnitPrice);
                var candidate = new PromotionEvaluation
                {
                    IsValid = true,
                    PromotionType = "COMBO",
                    Code = combo.Code,
                    DiscountAmount = allowed,
                    ComboOfferID = combo.ComboOfferID,
                    ProductID_A = combo.ProductID_A,
                    ProductID_B = combo.ProductID_B,
                    ProductNameA = combo.ProductNameA,
                    ProductNameB = combo.ProductNameB,
                    OriginalAmount = currentPairAmount,
                    Message = "Combo tốt nhất đã được tự động áp dụng."
                };
                if (best == null || candidate.DiscountAmount > best.DiscountAmount)
                    best = candidate;
            }
            return best ?? Invalid("Giỏ hàng chưa đủ điều kiện combo.");
        }

        public PromotionEvaluation EvaluatePromotion(string code, int customerId, IEnumerable<CartViewItem> items)
        {
            return EvaluateBestPromotion(code, customerId, items);
        }

        public PromotionEvaluation EvaluateBestPromotion(string selectedCode, int customerId, IEnumerable<CartViewItem> items)
        {
            var cartItems = (items ?? Enumerable.Empty<CartViewItem>()).Where(x => x.Quantity > 0).ToList();
            if (!cartItems.Any() || !SchemaExists()) return Invalid("Giỏ hàng hoặc schema ưu đãi không hợp lệ.");

            var combo = GetBestCombo(cartItems);
            PromotionEvaluation voucher = null;
            string voucherError = null;

            if (!string.IsNullOrWhiteSpace(selectedCode))
            {
                var code = selectedCode.Trim().ToUpperInvariant();
                var personal = db.Database.SqlQuery<PersonalPromotionRow>(@"
                    SELECT TOP 1 cc.CustomerCouponID, cc.CouponID, cc.TargetProductID,
                           c.Code, COALESCE(c.FixedDiscountAmount, c.MaxDiscountAmount, 0) DiscountAmount,
                           COALESCE(mc.MinimumMarginPct, 5) MinimumMarginPct
                    FROM dbo.CustomerCoupon cc
                    INNER JOIN dbo.Coupon c ON c.CouponID = cc.CouponID
                    LEFT JOIN dbo.MarketingCampaign mc ON mc.CampaignID = c.CampaignID
                    WHERE cc.CustomerID = @customerId AND c.Code = @code
                      AND cc.Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                      AND cc.ExpiresAt > SYSUTCDATETIME()
                      AND c.IsActive = 1 AND c.UsageLimit > 0;",
                    new SqlParameter("@customerId", customerId), new SqlParameter("@code", code)).FirstOrDefault();

                if (personal != null)
                {
                    var targetItems = personal.TargetProductID.HasValue
                        ? cartItems.Where(x => x.ProductID == personal.TargetProductID.Value).ToList()
                        : new List<CartViewItem>();
                    if (!targetItems.Any())
                    {
                        voucherError = "Voucher này chỉ áp dụng cho sản phẩm được chỉ định.";
                    }
                    else
                    {
                        var allowed = ApplyMarginGuard(personal.DiscountAmount, targetItems, personal.MinimumMarginPct);
                        if (allowed > 0m)
                        {
                            voucher = new PromotionEvaluation
                            {
                                IsValid = true, PromotionType = "PERSONAL", Code = code,
                                DiscountAmount = allowed, CouponID = personal.CouponID,
                                CustomerCouponID = personal.CustomerCouponID,
                                OriginalAmount = cartItems.Sum(x => x.TotalPrice),
                                Message = "Voucher cá nhân hợp lệ."
                            };
                        }
                        else voucherError = "Voucher không đạt biên lợi nhuận tối thiểu.";
                    }
                }
                else
                {
                    var global = db.Coupons.FirstOrDefault(c => c.Code == code);
                    if (global == null || global.ExpiryDate <= DateTime.Now || global.UsageLimit <= 0 || global.Products.Any())
                    {
                        voucherError = "Mã voucher không tồn tại, đã hết hạn hoặc không áp dụng cho toàn đơn.";
                    }
                    else
                    {
                        var cartTotal = cartItems.Sum(x => x.TotalPrice);
                        var requested = global.DiscountPercentage.GetValueOrDefault() > 0m
                            ? cartTotal * global.DiscountPercentage.Value / 100m : 0m;
                        if (global.MaxDiscountAmount.GetValueOrDefault() > 0m)
                            requested = requested <= 0m ? global.MaxDiscountAmount.Value : Math.Min(requested, global.MaxDiscountAmount.Value);
                        var allowed = ApplyMarginGuard(requested, cartItems, 5m);
                        if (allowed > 0m)
                        {
                            voucher = new PromotionEvaluation
                            {
                                IsValid = true, PromotionType = "GLOBAL", Code = code,
                                DiscountAmount = allowed, CouponID = global.CouponID,
                                OriginalAmount = cartTotal, Message = "Voucher toàn đơn hợp lệ."
                            };
                        }
                        else voucherError = "Voucher không đạt biên lợi nhuận tối thiểu.";
                    }
                }
            }

            if (voucher != null && (!combo.IsValid || voucher.DiscountAmount >= combo.DiscountAmount))
            {
                voucher.Message = combo.IsValid
                    ? "Đã chọn voucher vì mức giảm bằng hoặc cao hơn combo tốt nhất."
                    : "Áp dụng voucher thành công.";
                return voucher;
            }
            if (combo.IsValid)
            {
                combo.Message = voucher != null
                    ? "Hệ thống chọn combo vì tiết kiệm hơn voucher " +
                      (combo.DiscountAmount - voucher.DiscountAmount).ToString("N0") + " ₫."
                    : (!string.IsNullOrEmpty(voucherError)
                        ? voucherError + " Hệ thống đã áp dụng combo tốt nhất thay thế."
                        : "Combo tốt nhất đã được tự động áp dụng.");
                return combo;
            }
            return Invalid(voucherError ?? "Không có ưu đãi phù hợp với giỏ hàng.");
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
            vm.Settings = GetSettings();
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

        public MarketingSettingsVM GetSettings()
        {
            EnsureSchema();
            var personal = GetOrCreateCampaignSettings("PERSONAL", null);
            var combo = GetOrCreateCampaignSettings("COMBO", null);
            return new MarketingSettingsVM
            {
                PersonalDiscountPct = personal.DiscountPercentage,
                PersonalMaxDiscountAmount = personal.MaxDiscountAmount,
                PersonalMinInterestScore = personal.MinInterestScore,
                VoucherValidityHours = personal.ValidityHours,
                VoucherCooldownDays = personal.CooldownDays,
                PersonalMinimumMarginPct = personal.MinimumMarginPct,
                ComboDiscountPct = combo.DiscountPercentage,
                ComboMaxDiscountAmount = combo.MaxDiscountAmount,
                ComboMinSupport = combo.MinSupport,
                ComboMinConfidence = combo.MinConfidence,
                ComboMinUtility = combo.MinUtility,
                ComboValidityDays = combo.ValidityDays,
                ComboUsageLimit = combo.UsageLimit,
                ComboMinimumMarginPct = combo.MinimumMarginPct
            };
        }

        public void SaveSettings(MarketingSettingsVM model, string updatedBy)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureSchema();
            var personal = GetOrCreateCampaignSettings("PERSONAL", updatedBy);
            var combo = GetOrCreateCampaignSettings("COMBO", updatedBy);

            db.Database.ExecuteSqlCommand(@"
                UPDATE dbo.MarketingCampaign SET
                    DiscountPercentage = @personalPct, MaxDiscountAmount = @personalMax,
                    MinInterestScore = @minScore, ValidityHours = @hours, CooldownDays = @cooldown,
                    MinimumMarginPct = @personalMargin, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @updatedBy
                WHERE CampaignID = @personalId;

                UPDATE dbo.MarketingCampaign SET
                    DiscountPercentage = @comboPct, MaxDiscountAmount = @comboMax,
                    MinSupport = @support, MinConfidence = @confidence, MinUtility = @utility,
                    ValidityDays = @days, UsageLimit = @usageLimit,
                    MinimumMarginPct = @comboMargin, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @updatedBy
                WHERE CampaignID = @comboId;",
                new SqlParameter("@personalPct", model.PersonalDiscountPct),
                new SqlParameter("@personalMax", model.PersonalMaxDiscountAmount),
                new SqlParameter("@minScore", model.PersonalMinInterestScore),
                new SqlParameter("@hours", model.VoucherValidityHours),
                new SqlParameter("@cooldown", model.VoucherCooldownDays),
                new SqlParameter("@personalMargin", model.PersonalMinimumMarginPct),
                new SqlParameter("@comboPct", model.ComboDiscountPct),
                new SqlParameter("@comboMax", model.ComboMaxDiscountAmount),
                new SqlParameter("@support", model.ComboMinSupport),
                new SqlParameter("@confidence", model.ComboMinConfidence),
                new SqlParameter("@utility", model.ComboMinUtility),
                new SqlParameter("@days", model.ComboValidityDays),
                new SqlParameter("@usageLimit", model.ComboUsageLimit),
                new SqlParameter("@comboMargin", model.ComboMinimumMarginPct),
                new SqlParameter("@updatedBy", (object)updatedBy ?? DBNull.Value),
                new SqlParameter("@personalId", personal.CampaignID),
                new SqlParameter("@comboId", combo.CampaignID));
        }

        private decimal ApplyMarginGuard(decimal requestedDiscount, List<CartViewItem> cartItems,
                                         decimal minimumMarginPct, bool singleUnitPerProduct = false)
        {
            decimal revenue = 0m;
            decimal fifoCost = 0m;
            foreach (var item in cartItems)
            {
                var needed = singleUnitPerProduct ? Math.Min(1, item.Quantity) : item.Quantity;
                revenue += singleUnitPerProduct ? item.UnitPrice * needed : item.TotalPrice;
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

            var maximumDiscount = revenue - fifoCost - revenue * minimumMarginPct / 100m;
            return RoundDownToThousand(Math.Max(0m, Math.Min(requestedDiscount, maximumDiscount)));
        }

        private CampaignSettingsRow GetOrCreateCampaignSettings(string type, string createdBy)
        {
            var existing = db.Database.SqlQuery<CampaignSettingsRow>(@"
                SELECT TOP 1 CampaignID, CampaignType, MinimumMarginPct,
                       DiscountPercentage, MaxDiscountAmount, MinInterestScore,
                       ValidityHours, CooldownDays, MinSupport, MinConfidence,
                       MinUtility, ValidityDays, UsageLimit
                FROM dbo.MarketingCampaign
                WHERE CampaignType = @type AND IsActive = 1 AND EndDate > SYSUTCDATETIME()
                ORDER BY CampaignID DESC;", new SqlParameter("@type", type)).FirstOrDefault();
            if (existing != null) return existing;

            var name = type == "PERSONAL" ? "Ưu đãi cá nhân tự động" : "Combo mua chung tự động";
            var id = Convert.ToInt32(db.Database.SqlQuery<decimal>(@"
                INSERT dbo.MarketingCampaign
                    (CampaignName, CampaignType, SourceType, StartDate, EndDate, MinimumMarginPct,
                     DiscountPercentage, MaxDiscountAmount, MinInterestScore, ValidityHours, CooldownDays,
                     MinSupport, MinConfidence, MinUtility, ValidityDays, UsageLimit, IsActive, CreatedBy)
                VALUES
                    (@name, @type, 'HYBRID', SYSUTCDATETIME(), DATEADD(YEAR, 10, SYSUTCDATETIME()), 5,
                     5, CASE WHEN @type = 'PERSONAL' THEN 150000 ELSE 300000 END,
                     8, 72, 7, 2, 0.20, 100000, 14, 100, 1, @createdBy);
                SELECT CAST(SCOPE_IDENTITY() AS DECIMAL(18,0));",
                new SqlParameter("@name", name), new SqlParameter("@type", type),
                new SqlParameter("@createdBy", (object)createdBy ?? DBNull.Value)).Single());

            return new CampaignSettingsRow
            {
                CampaignID = id,
                CampaignType = type,
                MinimumMarginPct = 5m,
                DiscountPercentage = 5m,
                MaxDiscountAmount = type == "PERSONAL" ? 150000m : 300000m,
                MinInterestScore = 8m,
                ValidityHours = 72,
                CooldownDays = 7,
                MinSupport = 2,
                MinConfidence = 0.20m,
                MinUtility = 100000m,
                ValidityDays = 14,
                UsageLimit = 100
            };
        }

        private void ExpireOldOffers()
        {
            db.Database.ExecuteSqlCommand(@"
                UPDATE dbo.CustomerCoupon SET Status = 'EXPIRED'
                WHERE Status IN ('ISSUED','VIEWED','CLICKED','CARTED') AND ExpiresAt <= SYSUTCDATETIME();

                ;WITH OpenVoucherRank AS
                (
                    SELECT CustomerCouponID,
                           ROW_NUMBER() OVER
                           (
                               PARTITION BY CustomerID
                               ORDER BY InterestScore DESC, AssignedAt DESC, CustomerCouponID DESC
                           ) AS VoucherRank
                    FROM dbo.CustomerCoupon
                    WHERE Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                      AND ExpiresAt > SYSUTCDATETIME()
                )
                UPDATE cc SET Status = 'EXPIRED'
                FROM dbo.CustomerCoupon cc
                INNER JOIN OpenVoucherRank ranked ON ranked.CustomerCouponID = cc.CustomerCouponID
                WHERE ranked.VoucherRank > 1;

                UPDATE dbo.Coupon SET IsActive = 0
                WHERE CouponType = 'PERSONAL' AND ExpiryDate <= GETDATE();
                UPDATE dbo.ComboOffer SET IsActive = 0 WHERE EndDate <= SYSUTCDATETIME();");
        }

        private bool SchemaExists()
        {
            return db.Database.SqlQuery<int>(@"
                SELECT CASE WHEN OBJECT_ID('dbo.CustomerCoupon','U') IS NOT NULL
                                  AND OBJECT_ID('dbo.ComboOffer','U') IS NOT NULL
                                  AND OBJECT_ID('dbo.OrderPromotion','U') IS NOT NULL
                                  AND OBJECT_ID('dbo.MarketingCampaign','U') IS NOT NULL
                                  AND COL_LENGTH('dbo.MarketingCampaign','DiscountPercentage') IS NOT NULL
                                  AND COL_LENGTH('dbo.MarketingCampaign','CooldownDays') IS NOT NULL
                                  AND COL_LENGTH('dbo.Coupon','CouponType') IS NOT NULL
                             THEN 1 ELSE 0 END").Single() == 1;
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
            public decimal MinimumMarginPct { get; set; }
        }

        private class CampaignSettingsRow
        {
            public int CampaignID { get; set; }
            public string CampaignType { get; set; }
            public decimal MinimumMarginPct { get; set; }
            public decimal DiscountPercentage { get; set; }
            public decimal MaxDiscountAmount { get; set; }
            public decimal MinInterestScore { get; set; }
            public int ValidityHours { get; set; }
            public int CooldownDays { get; set; }
            public int MinSupport { get; set; }
            public decimal MinConfidence { get; set; }
            public decimal MinUtility { get; set; }
            public int ValidityDays { get; set; }
            public int UsageLimit { get; set; }
        }
    }
}
