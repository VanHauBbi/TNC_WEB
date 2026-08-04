/*
    TNC Store - Cài Marketing Selling trên database MyStore HIỆN CÓ.

    Script này KHÔNG DROP DATABASE, KHÔNG DROP TABLE và KHÔNG xóa dữ liệu.
    Nó chỉ:
      1. Tạo 4 bảng mới: MarketingCampaign, CustomerCoupon, ComboOffer, OrderPromotion.
      2. Bổ sung các cột marketing còn thiếu vào bảng Coupon hiện có.
      3. Tạo các khóa ngoại và index phục vụ truy vấn.

    SmartRecommendation và UserBehaviorLogs đã có trong database gốc nên được tái sử dụng.
    Có thể chạy lại script an toàn nếu lần chạy trước bị gián đoạn.
*/
USE [MyStore];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Dừng ngay nếu đang chạy nhầm database hoặc database gốc chưa đủ bảng.
IF OBJECT_ID('dbo.Coupon', 'U') IS NULL
    THROW 50001, N'Không tìm thấy bảng dbo.Coupon trong database MyStore.', 1;
IF OBJECT_ID('dbo.Customer', 'U') IS NULL
    THROW 50002, N'Không tìm thấy bảng dbo.Customer trong database MyStore.', 1;
IF OBJECT_ID('dbo.Product', 'U') IS NULL
    THROW 50003, N'Không tìm thấy bảng dbo.Product trong database MyStore.', 1;
IF OBJECT_ID('dbo.[Order]', 'U') IS NULL
    THROW 50004, N'Không tìm thấy bảng dbo.Order trong database MyStore.', 1;
IF OBJECT_ID('dbo.SmartRecommendation', 'U') IS NULL
    THROW 50005, N'Không tìm thấy bảng dbo.SmartRecommendation trong database MyStore.', 1;
IF OBJECT_ID('dbo.UserBehaviorLogs', 'U') IS NULL
    THROW 50006, N'Không tìm thấy bảng dbo.UserBehaviorLogs trong database MyStore.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.MarketingCampaign', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.MarketingCampaign
        (
            CampaignID       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingCampaign PRIMARY KEY,
            CampaignName     NVARCHAR(200) NOT NULL,
            CampaignType     VARCHAR(30) NOT NULL,
            SourceType       VARCHAR(30) NOT NULL CONSTRAINT DF_MarketingCampaign_Source DEFAULT ('MANUAL'),
            StartDate        DATETIME2(0) NOT NULL,
            EndDate          DATETIME2(0) NOT NULL,
            MinimumMarginPct DECIMAL(5,2) NOT NULL CONSTRAINT DF_MarketingCampaign_Margin DEFAULT (5),
            IsActive         BIT NOT NULL CONSTRAINT DF_MarketingCampaign_Active DEFAULT (1),
            CreatedBy        NVARCHAR(255) NULL,
            CreatedAt        DATETIME2(0) NOT NULL CONSTRAINT DF_MarketingCampaign_Created DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT CK_MarketingCampaign_Type CHECK (CampaignType IN ('PERSONAL', 'COMBO')),
            CONSTRAINT CK_MarketingCampaign_Dates CHECK (EndDate > StartDate),
            CONSTRAINT CK_MarketingCampaign_Margin CHECK (MinimumMarginPct BETWEEN 0 AND 100)
        );
    END;

    IF COL_LENGTH('dbo.Coupon', 'CouponType') IS NULL
        ALTER TABLE dbo.Coupon ADD CouponType VARCHAR(20) NOT NULL CONSTRAINT DF_Coupon_Type DEFAULT ('GLOBAL');
    IF COL_LENGTH('dbo.Coupon', 'DiscountType') IS NULL
        ALTER TABLE dbo.Coupon ADD DiscountType VARCHAR(20) NOT NULL CONSTRAINT DF_Coupon_DiscountType DEFAULT ('PERCENT');
    IF COL_LENGTH('dbo.Coupon', 'FixedDiscountAmount') IS NULL
        ALTER TABLE dbo.Coupon ADD FixedDiscountAmount DECIMAL(18,3) NULL;
    IF COL_LENGTH('dbo.Coupon', 'MinimumOrderValue') IS NULL
        ALTER TABLE dbo.Coupon ADD MinimumOrderValue DECIMAL(18,3) NOT NULL CONSTRAINT DF_Coupon_MinOrder DEFAULT (0);
    IF COL_LENGTH('dbo.Coupon', 'StartDate') IS NULL
        ALTER TABLE dbo.Coupon ADD StartDate DATETIME2(0) NULL;
    IF COL_LENGTH('dbo.Coupon', 'IsActive') IS NULL
        ALTER TABLE dbo.Coupon ADD IsActive BIT NOT NULL CONSTRAINT DF_Coupon_Active DEFAULT (1);
    IF COL_LENGTH('dbo.Coupon', 'IsStackable') IS NULL
        ALTER TABLE dbo.Coupon ADD IsStackable BIT NOT NULL CONSTRAINT DF_Coupon_Stackable DEFAULT (0);
    IF COL_LENGTH('dbo.Coupon', 'CampaignID') IS NULL
        ALTER TABLE dbo.Coupon ADD CampaignID INT NULL;
    IF COL_LENGTH('dbo.Coupon', 'SourceType') IS NULL
        ALTER TABLE dbo.Coupon ADD SourceType VARCHAR(30) NOT NULL CONSTRAINT DF_Coupon_Source DEFAULT ('MANUAL');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Coupon_MarketingCampaign')
        ALTER TABLE dbo.Coupon WITH CHECK ADD CONSTRAINT FK_Coupon_MarketingCampaign
            FOREIGN KEY (CampaignID) REFERENCES dbo.MarketingCampaign(CampaignID);

    IF OBJECT_ID('dbo.CustomerCoupon', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.CustomerCoupon
        (
            CustomerCouponID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerCoupon PRIMARY KEY,
            CouponID         INT NOT NULL,
            CustomerID       INT NOT NULL,
            TargetProductID  INT NULL,
            TriggerType      VARCHAR(40) NOT NULL,
            InterestScore    DECIMAL(10,2) NOT NULL,
            Status           VARCHAR(20) NOT NULL CONSTRAINT DF_CustomerCoupon_Status DEFAULT ('ISSUED'),
            AssignedAt       DATETIME2(0) NOT NULL CONSTRAINT DF_CustomerCoupon_Assigned DEFAULT (SYSUTCDATETIME()),
            ViewedAt         DATETIME2(0) NULL,
            ClickedAt        DATETIME2(0) NULL,
            AddedToCartAt    DATETIME2(0) NULL,
            UsedAt           DATETIME2(0) NULL,
            ExpiresAt        DATETIME2(0) NOT NULL,
            OrderID          INT NULL,
            CONSTRAINT FK_CustomerCoupon_Coupon FOREIGN KEY (CouponID) REFERENCES dbo.Coupon(CouponID),
            CONSTRAINT FK_CustomerCoupon_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID),
            CONSTRAINT FK_CustomerCoupon_Product FOREIGN KEY (TargetProductID) REFERENCES dbo.Product(ProductID),
            CONSTRAINT FK_CustomerCoupon_Order FOREIGN KEY (OrderID) REFERENCES dbo.[Order](OrderID),
            CONSTRAINT CK_CustomerCoupon_Status CHECK (Status IN ('ISSUED','VIEWED','CLICKED','CARTED','USED','EXPIRED'))
        );
    END;

    IF OBJECT_ID('dbo.ComboOffer', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ComboOffer
        (
            ComboOfferID         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ComboOffer PRIMARY KEY,
            CampaignID           INT NULL,
            ProductID_A          INT NOT NULL,
            ProductID_B          INT NOT NULL,
            Code                 NVARCHAR(50) NOT NULL,
            DiscountAmount       DECIMAL(18,3) NOT NULL,
            Support              INT NOT NULL,
            Confidence           DECIMAL(5,2) NOT NULL,
            ActualUtility        DECIMAL(18,3) NOT NULL,
            MinimumMarginPct     DECIMAL(5,2) NOT NULL CONSTRAINT DF_ComboOffer_Margin DEFAULT (5),
            StartDate            DATETIME2(0) NOT NULL,
            EndDate              DATETIME2(0) NOT NULL,
            UsageLimit           INT NOT NULL CONSTRAINT DF_ComboOffer_Limit DEFAULT (100),
            IsActive             BIT NOT NULL CONSTRAINT DF_ComboOffer_Active DEFAULT (1),
            CreatedAt            DATETIME2(0) NOT NULL CONSTRAINT DF_ComboOffer_Created DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT FK_ComboOffer_Campaign FOREIGN KEY (CampaignID) REFERENCES dbo.MarketingCampaign(CampaignID),
            CONSTRAINT FK_ComboOffer_ProductA FOREIGN KEY (ProductID_A) REFERENCES dbo.Product(ProductID),
            CONSTRAINT FK_ComboOffer_ProductB FOREIGN KEY (ProductID_B) REFERENCES dbo.Product(ProductID),
            CONSTRAINT UQ_ComboOffer_Code UNIQUE (Code),
            CONSTRAINT CK_ComboOffer_Products CHECK (ProductID_A <> ProductID_B),
            CONSTRAINT CK_ComboOffer_Discount CHECK (DiscountAmount > 0),
            CONSTRAINT CK_ComboOffer_Dates CHECK (EndDate > StartDate)
        );
    END;

    IF OBJECT_ID('dbo.OrderPromotion', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.OrderPromotion
        (
            OrderPromotionID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderPromotion PRIMARY KEY,
            OrderID          INT NOT NULL,
            PromotionType    VARCHAR(20) NOT NULL,
            CouponID         INT NULL,
            CustomerCouponID INT NULL,
            ComboOfferID     INT NULL,
            PromotionCode    NVARCHAR(50) NOT NULL,
            DiscountAmount   DECIMAL(18,3) NOT NULL,
            CreatedAt        DATETIME2(0) NOT NULL CONSTRAINT DF_OrderPromotion_Created DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT FK_OrderPromotion_Order FOREIGN KEY (OrderID) REFERENCES dbo.[Order](OrderID),
            CONSTRAINT FK_OrderPromotion_Coupon FOREIGN KEY (CouponID) REFERENCES dbo.Coupon(CouponID),
            CONSTRAINT FK_OrderPromotion_CustomerCoupon FOREIGN KEY (CustomerCouponID) REFERENCES dbo.CustomerCoupon(CustomerCouponID),
            CONSTRAINT FK_OrderPromotion_ComboOffer FOREIGN KEY (ComboOfferID) REFERENCES dbo.ComboOffer(ComboOfferID),
            CONSTRAINT CK_OrderPromotion_Type CHECK (PromotionType IN ('GLOBAL','PERSONAL','COMBO')),
            CONSTRAINT CK_OrderPromotion_Discount CHECK (DiscountAmount >= 0)
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserBehaviorLogs_CustomerProductDate' AND object_id = OBJECT_ID('dbo.UserBehaviorLogs'))
        CREATE INDEX IX_UserBehaviorLogs_CustomerProductDate
            ON dbo.UserBehaviorLogs(CustomerID, ProductID, CreatedAt) INCLUDE(ActionType, ActionWeight);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerCoupon_CustomerStatus' AND object_id = OBJECT_ID('dbo.CustomerCoupon'))
        CREATE INDEX IX_CustomerCoupon_CustomerStatus
            ON dbo.CustomerCoupon(CustomerID, Status, ExpiresAt) INCLUDE(CouponID, TargetProductID, InterestScore);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerCoupon_Target' AND object_id = OBJECT_ID('dbo.CustomerCoupon'))
        CREATE INDEX IX_CustomerCoupon_Target
            ON dbo.CustomerCoupon(CustomerID, TargetProductID, ExpiresAt) INCLUDE(Status, CouponID);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ComboOffer_ActiveDates' AND object_id = OBJECT_ID('dbo.ComboOffer'))
        CREATE INDEX IX_ComboOffer_ActiveDates
            ON dbo.ComboOffer(IsActive, StartDate, EndDate) INCLUDE(ProductID_A, ProductID_B, DiscountAmount, Code);

    COMMIT TRANSACTION;
    PRINT N'Marketing Selling schema installed successfully.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
