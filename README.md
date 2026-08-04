# TNC_WEB

ASP.NET MVC 5 / .NET Framework 4.7.2 computer retail system using Entity Framework 6 Database-First.

## Marketing Selling setup

1. Pull the feature branch and restore NuGet packages in Visual Studio.
2. Back up the `MyStore` database.
3. Update the `MyStore` database with the Marketing Selling tables and Coupon columns before starting the application.
4. Update the `MyStoreEntities` connection string in `WebBanHang/Web.config` for your SQL Server instance.
5. Fill development-only values for SMTP, GHN and VNPay. Do not commit real secrets.
6. Build the solution, sign in as Admin, then open **Marketing Selling**.
7. Run **Tạo voucher cá nhân** and **Tạo combo Hybrid** after the system has behavior and order data.

The marketing subsystem intentionally uses parameterized SQL through the existing EF connection. You do not need to run **Update Model from Database** after installing the script, and existing generated EDMX entities remain untouched.

## Promotion rules

- Personal vouchers are generated from recent `VIEW`, `CLICK`, `DWELL_TIME`, `ADD_CART` and `REMOVE_CART` events.
- A recent `PURCHASE` excludes the same product from personal voucher generation.
- Combo candidates come from `SmartRecommendation` and require both Apriori frequency/confidence and Two-Phase utility.
- Every personal/combo discount is recalculated server-side and capped by the projected FIFO margin with a 5% minimum margin.
- Personal codes are bound to one `CustomerID`; combo codes require both products in the cart.

## Security configuration

Password storage now uses PBKDF2-SHA256. Existing SHA-256 passwords are upgraded automatically after a successful login. Password-reset links require a random token sent by SMTP and expire after 30 minutes.

The GHN token that previously existed in Git history must be revoked and replaced. Configure all secrets outside source control.
