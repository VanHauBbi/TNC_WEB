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
                    Message = "Combo tốt nhất đã được giảm một lần trên tổng đơn."
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
                    var now = DateTime.Now;
                    var global = db.Coupons.FirstOrDefault(c => c.Code == code
                                                             && c.CouponType == "GLOBAL"
                                                             && c.IsActive);
                    if (global == null || global.ExpiryDate <= now || global.UsageLimit <= 0
                        || (global.StartDate.HasValue && global.StartDate.Value > now)
                        || global.Products.Any())
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
                        : "Combo tốt nhất đã được giảm một lần trên tổng đơn.");
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
            var now = DateTime.Now;
            if (!SchemaExists())
                return db.Coupons.Where(c => c.ExpiryDate > now && c.UsageLimit > 0 && !c.Products.Any())
                    .OrderByDescending(c => c.CouponID).ToList();

            // Coupon.StartDate/ExpiryDate được trang Admin lưu theo giờ local.
            // Dùng cùng một mốc giờ để mã vừa tạo không bị ẩn 7 giờ do so với UTC.
            return db.Coupons.Where(c => c.CouponType == "GLOBAL"
                                         && c.IsActive
                                         && (!c.StartDate.HasValue || c.StartDate.Value <= now)
                                         && c.ExpiryDate > now
                                         && c.UsageLimit > 0
                                         && !c.Products.Any())
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

        public PersonalVoucherAdminVM GetPersonalVoucherAdmin(int customerCouponId)
        {
            EnsureSchema();
            var item = db.Database.SqlQuery<PersonalVoucherAdminVM>(@"
                SELECT cc.CustomerCouponID, cc.CouponID, cc.CustomerID, cc.TargetProductID,
                       customer.CustomerName, customer.CustomerEmail,
                       product.ProductName, product.ProductImage,
                       coupon.CouponName, coupon.Code,
                       CAST(COALESCE(coupon.FixedDiscountAmount, coupon.MaxDiscountAmount, 0) AS DECIMAL(18,3)) DiscountAmount,
                       cc.InterestScore, cc.TriggerType, cc.Status,
                       CAST(cc.AssignedAt AS DATETIME) AssignedAt,
                       CAST(cc.ViewedAt AS DATETIME) ViewedAt,
                       CAST(cc.ClickedAt AS DATETIME) ClickedAt,
                       CAST(cc.AddedToCartAt AS DATETIME) AddedToCartAt,
                       CAST(cc.UsedAt AS DATETIME) UsedAt,
                       CAST(cc.ExpiresAt AS DATETIME) ExpiresAt,
                       cc.OrderID, coupon.UsageLimit,
                       CAST(COALESCE(campaign.MinimumMarginPct, 5) AS DECIMAL(5,2)) MinimumMarginPct,
                       CAST(coupon.IsActive AS BIT) CouponIsActive
                FROM dbo.CustomerCoupon cc
                INNER JOIN dbo.Coupon coupon ON coupon.CouponID = cc.CouponID
                INNER JOIN dbo.Customer customer ON customer.CustomerID = cc.CustomerID
                LEFT JOIN dbo.Product product ON product.ProductID = cc.TargetProductID
                LEFT JOIN dbo.MarketingCampaign campaign ON campaign.CampaignID = coupon.CampaignID
                WHERE cc.CustomerCouponID = @id;",
                new SqlParameter("@id", customerCouponId)).FirstOrDefault();
            if (item != null)
            {
                item.AssignedAt = AsLocalTime(item.AssignedAt);
                item.ViewedAt = AsLocalTime(item.ViewedAt);
                item.ClickedAt = AsLocalTime(item.ClickedAt);
                item.AddedToCartAt = AsLocalTime(item.AddedToCartAt);
                item.UsedAt = AsLocalTime(item.UsedAt);
                item.ExpiresAt = AsLocalTime(item.ExpiresAt);
            }
            return item;
        }

        public PersonalVoucherEditVM GetPersonalVoucherEdit(int customerCouponId)
        {
            var item = GetPersonalVoucherAdmin(customerCouponId);
            if (item == null) return null;
            return new PersonalVoucherEditVM
            {
                CustomerCouponID = item.CustomerCouponID,
                Code = item.Code,
                CustomerName = item.CustomerName,
                ProductName = item.ProductName,
                DiscountAmount = item.DiscountAmount,
                ExpiresAt = item.ExpiresAt,
                IsActive = item.CouponIsActive && item.Status != "EXPIRED" && item.Status != "USED"
            };
        }

        public decimal UpdatePersonalVoucher(PersonalVoucherEditVM model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var current = GetPersonalVoucherAdmin(model.CustomerCouponID);
            if (current == null) throw new InvalidOperationException("Không tìm thấy voucher cá nhân.");
            if (current.Status == "USED") throw new InvalidOperationException("Voucher đã sử dụng chỉ được xem lịch sử, không thể chỉnh sửa.");

            var product = current.TargetProductID.HasValue ? db.Products.Find(current.TargetProductID.Value) : null;
            if (product == null) throw new InvalidOperationException("Không tìm thấy sản phẩm của voucher.");
            var requestedItems = new List<CartViewItem>
            {
                new CartViewItem
                {
                    ProductID = product.ProductID, ProductName = product.ProductName, Quantity = 1,
                    UnitPrice = product.ProductPrice, OriginalPrice = product.ProductPrice
                }
            };
            var safeDiscount = ApplyMarginGuard(model.DiscountAmount, requestedItems, current.MinimumMarginPct);
            if (safeDiscount <= 0m)
                throw new InvalidOperationException("Sản phẩm không còn đủ lợi nhuận FIFO để áp dụng voucher.");

            var expiresLocal = DateTime.SpecifyKind(model.ExpiresAt, DateTimeKind.Local);
            var expiresUtc = expiresLocal.ToUniversalTime();
            if (model.IsActive && expiresUtc <= DateTime.UtcNow)
                throw new InvalidOperationException("Voucher đang hoạt động phải có hạn sử dụng trong tương lai.");
            if (model.IsActive)
            {
                var otherActive = db.Database.SqlQuery<int>(@"
                    SELECT COUNT(1) FROM dbo.CustomerCoupon
                    WHERE CustomerID = @customerId AND CustomerCouponID <> @id
                      AND Status IN ('ISSUED','VIEWED','CLICKED','CARTED')
                      AND ExpiresAt > SYSUTCDATETIME();",
                    new SqlParameter("@customerId", current.CustomerID),
                    new SqlParameter("@id", current.CustomerCouponID)).Single();
                if (otherActive > 0)
                    throw new InvalidOperationException("Khách hàng đã có một voucher khác đang hoạt động.");
            }

            db.Database.ExecuteSqlCommand(@"
                UPDATE dbo.Coupon
                SET FixedDiscountAmount = @discount, MaxDiscountAmount = @discount,
                    ExpiryDate = @expiresLocal, IsActive = @active
                WHERE CouponID = @couponId;

                UPDATE dbo.CustomerCoupon
                SET ExpiresAt = @expiresUtc,
                    Status = CASE
                        WHEN @active = 0 THEN 'EXPIRED'
                        WHEN Status = 'EXPIRED' THEN 'ISSUED'
                        ELSE Status END
                WHERE CustomerCouponID = @id;",
                new SqlParameter("@discount", safeDiscount),
                new SqlParameter("@expiresLocal", expiresLocal),
                new SqlParameter("@expiresUtc", expiresUtc),
                new SqlParameter("@active", model.IsActive),
                new SqlParameter("@couponId", current.CouponID),
                new SqlParameter("@id", current.CustomerCouponID));
            return safeDiscount;
        }

        public bool DeactivatePersonalVoucher(int customerCouponId)
        {
            EnsureSchema();
            return db.Database.ExecuteSqlCommand(@"
                UPDATE coupon SET IsActive = 0
                FROM dbo.Coupon coupon
                INNER JOIN dbo.CustomerCoupon cc ON cc.CouponID = coupon.CouponID
                WHERE cc.CustomerCouponID = @id;

                UPDATE dbo.CustomerCoupon SET Status = 'EXPIRED'
                WHERE CustomerCouponID = @id AND Status <> 'USED';",
                new SqlParameter("@id", customerCouponId)) > 0;
        }

        public ComboOfferAdminVM GetComboOfferAdmin(int comboOfferId)
        {
            EnsureSchema();
            var item = db.Database.SqlQuery<ComboOfferAdminVM>(@"
                SELECT combo.ComboOfferID, combo.ProductID_A, combo.ProductID_B,
                       productA.ProductName ProductNameA, productB.ProductName ProductNameB,
                       productA.ProductImage ProductImageA, productB.ProductImage ProductImageB,
                       productA.ProductPrice ProductPriceA, productB.ProductPrice ProductPriceB,
                       combo.Code, combo.DiscountAmount, combo.Support, combo.Confidence,
                       combo.ActualUtility, combo.MinimumMarginPct,
                       CAST(combo.StartDate AS DATETIME) StartDate,
                       CAST(combo.EndDate AS DATETIME) EndDate,
                       combo.UsageLimit, combo.IsActive,
                       CAST(combo.CreatedAt AS DATETIME) CreatedAt,
                       (SELECT COUNT(1) FROM dbo.OrderPromotion op WHERE op.ComboOfferID = combo.ComboOfferID) UsedOrderCount,
                       CAST(COALESCE((SELECT SUM(op.DiscountAmount) FROM dbo.OrderPromotion op WHERE op.ComboOfferID = combo.ComboOfferID), 0) AS DECIMAL(18,3)) UsedDiscountTotal
                FROM dbo.ComboOffer combo
                INNER JOIN dbo.Product productA ON productA.ProductID = combo.ProductID_A
                INNER JOIN dbo.Product productB ON productB.ProductID = combo.ProductID_B
                WHERE combo.ComboOfferID = @id;",
                new SqlParameter("@id", comboOfferId)).FirstOrDefault();
            if (item != null)
            {
                item.StartDate = AsLocalTime(item.StartDate);
                item.EndDate = AsLocalTime(item.EndDate);
                item.CreatedAt = AsLocalTime(item.CreatedAt);
            }
            return item;
        }

        public List<ComboOfferAdminVM> GetRecentComboOffersAdmin()
        {
            EnsureSchema();
            var items = db.Database.SqlQuery<ComboOfferAdminVM>(@"
                SELECT TOP 30 combo.ComboOfferID, combo.ProductID_A, combo.ProductID_B,
                       productA.ProductName ProductNameA, productB.ProductName ProductNameB,
                       productA.ProductImage ProductImageA, productB.ProductImage ProductImageB,
                       productA.ProductPrice ProductPriceA, productB.ProductPrice ProductPriceB,
                       combo.Code, combo.DiscountAmount, combo.Support, combo.Confidence,
                       combo.ActualUtility, combo.MinimumMarginPct,
                       CAST(combo.StartDate AS DATETIME) StartDate,
                       CAST(combo.EndDate AS DATETIME) EndDate,
                       combo.UsageLimit, combo.IsActive,
                       CAST(combo.CreatedAt AS DATETIME) CreatedAt,
                       (SELECT COUNT(1) FROM dbo.OrderPromotion op WHERE op.ComboOfferID = combo.ComboOfferID) UsedOrderCount,
                       CAST(COALESCE((SELECT SUM(op.DiscountAmount) FROM dbo.OrderPromotion op WHERE op.ComboOfferID = combo.ComboOfferID), 0) AS DECIMAL(18,3)) UsedDiscountTotal
                FROM dbo.ComboOffer combo
                INNER JOIN dbo.Product productA ON productA.ProductID = combo.ProductID_A
                INNER JOIN dbo.Product productB ON productB.ProductID = combo.ProductID_B
                ORDER BY combo.CreatedAt DESC, combo.ComboOfferID DESC;").ToList();
            foreach (var item in items)
            {
                item.StartDate = AsLocalTime(item.StartDate);
                item.EndDate = AsLocalTime(item.EndDate);
                item.CreatedAt = AsLocalTime(item.CreatedAt);
            }
            return items;
        }

        public ComboOfferEditVM GetComboOfferEdit(int comboOfferId)
        {
            var item = GetComboOfferAdmin(comboOfferId);
            if (item == null) return null;
            return new ComboOfferEditVM
            {
                ComboOfferID = item.ComboOfferID,
                Code = item.Code,
                ProductNameA = item.ProductNameA,
                ProductNameB = item.ProductNameB,
                DiscountAmount = item.DiscountAmount,
                StartDate = item.StartDate,
                EndDate = item.EndDate,
                UsageLimit = item.UsageLimit,
                IsActive = item.IsActive
            };
        }

        public decimal UpdateComboOffer(ComboOfferEditVM model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var current = GetComboOfferAdmin(model.ComboOfferID);
            if (current == null) throw new InvalidOperationException("Không tìm thấy combo.");

            var products = db.Products.Where(p => p.ProductID == current.ProductID_A || p.ProductID == current.ProductID_B).ToList();
            if (products.Count != 2) throw new InvalidOperationException("Không tìm thấy đầy đủ sản phẩm của combo.");
            var requestedItems = products.Select(product => new CartViewItem
            {
                ProductID = product.ProductID, ProductName = product.ProductName, Quantity = 1,
                UnitPrice = product.ProductPrice, OriginalPrice = product.ProductPrice
            }).ToList();
            var safeDiscount = ApplyMarginGuard(model.DiscountAmount, requestedItems, current.MinimumMarginPct, true);
            if (safeDiscount <= 0m)
                throw new InvalidOperationException("Cặp sản phẩm không còn đủ lợi nhuận FIFO để áp dụng combo.");

            var startUtc = DateTime.SpecifyKind(model.StartDate, DateTimeKind.Local).ToUniversalTime();
            var endUtc = DateTime.SpecifyKind(model.EndDate, DateTimeKind.Local).ToUniversalTime();
            if (endUtc <= startUtc) throw new InvalidOperationException("Ngày kết thúc phải sau ngày bắt đầu.");
            if (model.IsActive && (endUtc <= DateTime.UtcNow || model.UsageLimit <= 0))
                throw new InvalidOperationException("Combo hoạt động phải còn hạn và còn lượt sử dụng.");
            if (model.IsActive)
            {
                var duplicateActive = db.Database.SqlQuery<int>(@"
                    SELECT COUNT(1) FROM dbo.ComboOffer
                    WHERE ComboOfferID <> @id AND IsActive = 1 AND EndDate > SYSUTCDATETIME()
                      AND ((ProductID_A = @a AND ProductID_B = @b)
                        OR (ProductID_A = @b AND ProductID_B = @a));",
                    new SqlParameter("@id", current.ComboOfferID),
                    new SqlParameter("@a", current.ProductID_A),
                    new SqlParameter("@b", current.ProductID_B)).Single();
                if (duplicateActive > 0)
                    throw new InvalidOperationException("Cặp sản phẩm này đã có một combo khác đang hoạt động.");
            }

            db.Database.ExecuteSqlCommand(@"
                UPDATE dbo.ComboOffer
                SET DiscountAmount = @discount, StartDate = @startDate, EndDate = @endDate,
                    UsageLimit = @usageLimit, IsActive = @active
                WHERE ComboOfferID = @id;",
                new SqlParameter("@discount", safeDiscount),
                new SqlParameter("@startDate", startUtc),
                new SqlParameter("@endDate", endUtc),
                new SqlParameter("@usageLimit", model.UsageLimit),
                new SqlParameter("@active", model.IsActive),
                new SqlParameter("@id", model.ComboOfferID));
            return safeDiscount;
        }

        public bool DeactivateComboOffer(int comboOfferId)
        {
            EnsureSchema();
            return db.Database.ExecuteSqlCommand(
                "UPDATE dbo.ComboOffer SET IsActive = 0 WHERE ComboOfferID = @id;",
                new SqlParameter("@id", comboOfferId)) > 0;
        }

        private static DateTime AsLocalTime(DateTime value)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();
        }

        private static DateTime? AsLocalTime(DateTime? value)
        {
            return value.HasValue ? AsLocalTime(value.Value) : (DateTime?)null;
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
            vm.ManagedCombos = GetRecentComboOffersAdmin();
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
                ComboMinConfidencePercent = combo.MinConfidence * 100m,
                ComboMinUtility = combo.MinUtility,
                ComboValidityDays = combo.ValidityDays,
                ComboUsageLimit = combo.UsageLimit,
                ComboMinimumMarginPct = combo.MinimumMarginPct
            };
        }

        public bool SaveSettings(MarketingSettingsVM model, string updatedBy)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureSchema();
            var personal = GetOrCreateCampaignSettings("PERSONAL", updatedBy);
            var combo = GetOrCreateCampaignSettings("COMBO", updatedBy);
            var normalizedConfidence = model.ComboMinConfidencePercent / 100m;
            var comboSettingsChanged = combo.DiscountPercentage != model.ComboDiscountPct
                || combo.MaxDiscountAmount != model.ComboMaxDiscountAmount
                || combo.MinSupport != model.ComboMinSupport
                || combo.MinConfidence != normalizedConfidence
                || combo.MinUtility != model.ComboMinUtility
                || combo.ValidityDays != model.ComboValidityDays
                || combo.UsageLimit != model.ComboUsageLimit
                || combo.MinimumMarginPct != model.ComboMinimumMarginPct;

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
                new SqlParameter("@confidence", normalizedConfidence),
                new SqlParameter("@utility", model.ComboMinUtility),
                new SqlParameter("@days", model.ComboValidityDays),
                new SqlParameter("@usageLimit", model.ComboUsageLimit),
                new SqlParameter("@comboMargin", model.ComboMinimumMarginPct),
                new SqlParameter("@updatedBy", (object)updatedBy ?? DBNull.Value),
                new SqlParameter("@personalId", personal.CampaignID),
                new SqlParameter("@comboId", combo.CampaignID));

            if (comboSettingsChanged)
            {
                // Combo cũ mang số tiền giảm được tính theo cấu hình trước đó.
                // Ngừng chúng để lần tạo tiếp theo luôn dùng đúng cấu hình vừa lưu.
                db.Database.ExecuteSqlCommand(@"
                    UPDATE dbo.ComboOffer
                    SET IsActive = 0
                    WHERE IsActive = 1 AND EndDate > SYSUTCDATETIME();");
            }
            return comboSettingsChanged;
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
