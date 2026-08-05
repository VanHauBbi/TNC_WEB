# TNC_WEB

ASP.NET MVC 5 / .NET Framework 4.7.2 computer retail system using Entity Framework 6 Database-First.

## Marketing Selling setup

1. Pull the feature branch and restore NuGet packages in Visual Studio.
2. Back up the `MyStore` database.
3. Update the `MyStore` database with the Marketing Selling tables and Coupon columns before starting the application.
4. Update the `MyStoreEntities` connection string in `WebBanHang/Web.config` for your SQL Server instance.
5. In `Model.edmx`, use **Update Model from Database**: add the four Marketing tables and refresh `Coupon`/`MarketingCampaign`.
6. Fill development-only values for SMTP, GHN, VNPay and `MarketingSchedulerKey`. Do not commit real secrets.
7. Build the solution, sign in as Admin, then open **Marketing Selling**.
8. Save campaign thresholds before running **Tạo voucher cá nhân** or **Tạo combo Hybrid**.

The marketing subsystem uses parameterized SQL through the existing EF connection. The scheduler endpoint remains disabled until a non-empty `MarketingSchedulerKey` is supplied in the deployment environment.

## Promotion rules

- Personal vouchers are generated from recent `VIEW`, `CLICK`, `DWELL_TIME`, `ADD_CART` and `REMOVE_CART` events.
- Repeated behavior is capped per user/product/day, and each customer can have only one active personal voucher.
- A recent `PURCHASE` excludes the same product from personal voucher generation.
- Combo candidates come from `SmartRecommendation` and require both Apriori frequency/confidence and Two-Phase utility.
- One order receives at most one promotion: the selected voucher or the best eligible combo, whichever saves more.
- Every discount is recalculated server-side and capped by the campaign's configured FIFO minimum margin.
- Personal codes are bound to one `CustomerID`; combo codes require both products in the cart.

## Security configuration

Password storage now uses PBKDF2-SHA256. Existing SHA-256 passwords are upgraded automatically after a successful login. Password-reset links require a random token sent by SMTP and expire after 30 minutes.

The GHN token that previously existed in Git history must be revoked and replaced. Configure all secrets outside source control.
