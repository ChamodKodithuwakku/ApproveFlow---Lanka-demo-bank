IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE SEQUENCE [LoanApplicationNumbers] START WITH 1 INCREMENT BY 1 NO CYCLE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [EventType] nvarchar(20) NOT NULL,
        [TableName] nvarchar(128) NULL,
        [RecordKey] nvarchar(100) NULL,
        [Action] nvarchar(50) NOT NULL,
        [OldValues] nvarchar(max) NULL,
        [NewValues] nvarchar(max) NULL,
        [ChangedColumns] nvarchar(2000) NULL,
        [UserId] nvarchar(450) NULL,
        [UserName] nvarchar(256) NULL,
        [IpAddress] nvarchar(45) NULL,
        [CorrelationId] nvarchar(100) NULL,
        [TimestampUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [Branches] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(10) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [City] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] nvarchar(450) NOT NULL,
        [CreatedByName] nvarchar(256) NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] nvarchar(450) NULL,
        [UpdatedByName] nvarchar(256) NULL,
        CONSTRAINT [PK_Branches] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [ImportBatches] (
        [Id] int NOT NULL IDENTITY,
        [FileName] nvarchar(260) NOT NULL,
        [FileHash] nvarchar(64) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [TotalRows] int NOT NULL,
        [ImportedRows] int NOT NULL,
        [RejectedRows] int NOT NULL,
        [TotalAmountInFile] decimal(18,2) NOT NULL,
        [ImportedAmount] decimal(18,2) NOT NULL,
        [RejectedAmount] decimal(18,2) NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] nvarchar(450) NOT NULL,
        [CreatedByName] nvarchar(256) NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] nvarchar(450) NULL,
        [UpdatedByName] nvarchar(256) NULL,
        CONSTRAINT [PK_ImportBatches] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [LoanProducts] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(10) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [MinAmount] decimal(18,2) NOT NULL,
        [MaxAmount] decimal(18,2) NOT NULL,
        [MinTenureMonths] int NOT NULL,
        [MaxTenureMonths] int NOT NULL,
        [DefaultInterestRate] decimal(5,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] nvarchar(450) NOT NULL,
        [CreatedByName] nvarchar(256) NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] nvarchar(450) NULL,
        [UpdatedByName] nvarchar(256) NULL,
        CONSTRAINT [PK_LoanProducts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LoanProducts_AmountRange] CHECK ([MinAmount] > 0 AND [MaxAmount] >= [MinAmount]),
        CONSTRAINT [CK_LoanProducts_InterestRate] CHECK ([DefaultInterestRate] >= 0 AND [DefaultInterestRate] <= 100),
        CONSTRAINT [CK_LoanProducts_TenureRange] CHECK ([MinTenureMonths] >= 1 AND [MaxTenureMonths] <= 360 AND [MaxTenureMonths] >= [MinTenureMonths])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [BranchId] int NOT NULL,
        [ApprovalLimit] decimal(18,2) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AspNetUsers_ApprovalLimit] CHECK ([ApprovalLimit] IS NULL OR [ApprovalLimit] >= 0),
        CONSTRAINT [FK_AspNetUsers_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] int NOT NULL IDENTITY,
        [CustomerNumber] nvarchar(20) NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [Nic] nvarchar(12) NOT NULL,
        [DateOfBirth] date NULL,
        [Phone] nvarchar(20) NULL,
        [Email] nvarchar(256) NULL,
        [Address] nvarchar(300) NULL,
        [BranchId] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] nvarchar(450) NOT NULL,
        [CreatedByName] nvarchar(256) NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] nvarchar(450) NULL,
        [UpdatedByName] nvarchar(256) NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Customers_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [ImportErrors] (
        [Id] int NOT NULL IDENTITY,
        [ImportBatchId] int NOT NULL,
        [RowNumber] int NOT NULL,
        [LegacyReference] nvarchar(50) NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [RawData] nvarchar(2000) NULL,
        CONSTRAINT [PK_ImportErrors] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ImportErrors_ImportBatches_ImportBatchId] FOREIGN KEY ([ImportBatchId]) REFERENCES [ImportBatches] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [LoanApplications] (
        [Id] int NOT NULL IDENTITY,
        [ReferenceNumber] nvarchar(30) NOT NULL,
        [CustomerId] int NOT NULL,
        [LoanProductId] int NOT NULL,
        [BranchId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [TenureMonths] int NOT NULL,
        [InterestRate] decimal(5,2) NOT NULL,
        [Purpose] nvarchar(500) NOT NULL,
        [MonthlyIncome] decimal(18,2) NOT NULL,
        [ExistingObligations] decimal(18,2) NOT NULL,
        [MonthlyInstallment] decimal(18,2) NOT NULL,
        [DebtBurdenRatio] decimal(9,2) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [LastEditedById] nvarchar(450) NOT NULL,
        [LastEditedByName] nvarchar(256) NOT NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [SubmittedById] nvarchar(450) NULL,
        [FirstApproverId] nvarchar(450) NULL,
        [FirstApproverName] nvarchar(256) NULL,
        [FirstApprovedAtUtc] datetime2 NULL,
        [DecidedById] nvarchar(450) NULL,
        [DecidedByName] nvarchar(256) NULL,
        [DecidedAtUtc] datetime2 NULL,
        [IsImported] bit NOT NULL,
        [ImportBatchId] int NULL,
        [RowVersion] rowversion NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedById] nvarchar(450) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] nvarchar(450) NOT NULL,
        [CreatedByName] nvarchar(256) NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] nvarchar(450) NULL,
        [UpdatedByName] nvarchar(256) NULL,
        CONSTRAINT [PK_LoanApplications] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LoanApplications_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_LoanApplications_InterestRate] CHECK ([InterestRate] >= 0 AND [InterestRate] <= 100),
        CONSTRAINT [CK_LoanApplications_MonthlyIncome] CHECK ([MonthlyIncome] > 0),
        CONSTRAINT [CK_LoanApplications_Obligations] CHECK ([ExistingObligations] >= 0),
        CONSTRAINT [CK_LoanApplications_Status] CHECK ([Status] IN ('Draft','Submitted','PendingSecondApproval','Approved','Rejected','ReturnedForCorrection')),
        CONSTRAINT [CK_LoanApplications_Tenure] CHECK ([TenureMonths] BETWEEN 1 AND 360),
        CONSTRAINT [FK_LoanApplications_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LoanApplications_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LoanApplications_ImportBatches_ImportBatchId] FOREIGN KEY ([ImportBatchId]) REFERENCES [ImportBatches] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LoanApplications_LoanProducts_LoanProductId] FOREIGN KEY ([LoanProductId]) REFERENCES [LoanProducts] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE TABLE [ApprovalActions] (
        [Id] int NOT NULL IDENTITY,
        [LoanApplicationId] int NOT NULL,
        [ActionType] nvarchar(30) NOT NULL,
        [FromStatus] nvarchar(30) NULL,
        [ToStatus] nvarchar(30) NOT NULL,
        [PerformedById] nvarchar(450) NOT NULL,
        [PerformedByName] nvarchar(256) NOT NULL,
        [Remarks] nvarchar(1000) NULL,
        [PerformedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ApprovalActions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ApprovalActions_LoanApplications_LoanApplicationId] FOREIGN KEY ([LoanApplicationId]) REFERENCES [LoanApplications] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ApprovalActions_ActionType_PerformedAtUtc] ON [ApprovalActions] ([ActionType], [PerformedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ApprovalActions_LoanApplicationId_PerformedAtUtc] ON [ApprovalActions] ([LoanApplicationId], [PerformedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ApprovalActions_PerformedById_PerformedAtUtc] ON [ApprovalActions] ([PerformedById], [PerformedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_BranchId] ON [AspNetUsers] ([BranchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EventType_Action] ON [AuditLogs] ([EventType], [Action]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TableName_RecordKey] ON [AuditLogs] ([TableName], [RecordKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TimestampUtc] ON [AuditLogs] ([TimestampUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId_TimestampUtc] ON [AuditLogs] ([UserId], [TimestampUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Branches_Code] ON [Branches] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Customers_BranchId] ON [Customers] ([BranchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Customers_CustomerNumber] ON [Customers] ([CustomerNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Customers_FullName] ON [Customers] ([FullName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Customers_Nic] ON [Customers] ([Nic]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ImportBatches_FileHash] ON [ImportBatches] ([FileHash]) WHERE [Status] = ''Completed''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ImportErrors_ImportBatchId_RowNumber] ON [ImportErrors] ([ImportBatchId], [RowNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoanApplications_BranchId] ON [LoanApplications] ([BranchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoanApplications_CreatedById] ON [LoanApplications] ([CreatedById]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoanApplications_CustomerId] ON [LoanApplications] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoanApplications_ImportBatchId] ON [LoanApplications] ([ImportBatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoanApplications_LoanProductId] ON [LoanApplications] ([LoanProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LoanApplications_ReferenceNumber] ON [LoanApplications] ([ReferenceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoanApplications_Status_CreatedAtUtc] ON [LoanApplications] ([Status], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoanApplications_Status_SubmittedAtUtc] ON [LoanApplications] ([Status], [SubmittedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LoanProducts_Code] ON [LoanProducts] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154751_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014154751_InitialCreate', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154810_AddReportingObjects'
)
BEGIN
    EXEC(N'/*
        dbo.vw_PendingApprovalsAging
        ---------------------------------------------------------------------------------------------
        Applications awaiting a checker decision (Submitted or PendingSecondApproval) with their age
        since the latest submission and an ageing bucket: ''0-2 days'', ''3-5 days'', ''6+ days''.
        Age is measured in whole elapsed days (24-hour periods), not calendar-day boundaries.

        Used by: dashboard ageing chart and approval queue.
        Source of truth: /database/views. Applied by EF Core migration AddReportingObjects.
    */
    CREATE OR ALTER VIEW dbo.vw_PendingApprovalsAging
    AS
    SELECT a.Id                 AS LoanApplicationId,
           a.ReferenceNumber,
           c.FullName           AS CustomerName,
           b.Id                 AS BranchId,
           b.Name               AS BranchName,
           p.Name               AS ProductName,
           a.Amount,
           a.Status,
           a.SubmittedAtUtc,
           a.FirstApproverName,
           age.AgeDays,
           CASE WHEN age.AgeDays <= 2 THEN N''0-2 days''
                WHEN age.AgeDays <= 5 THEN N''3-5 days''
                ELSE N''6+ days''
           END                  AS AgeBucket,
           CASE WHEN age.AgeDays <= 2 THEN 1
                WHEN age.AgeDays <= 5 THEN 2
                ELSE 3
           END                  AS AgeBucketOrder
    FROM dbo.LoanApplications AS a
    INNER JOIN dbo.Customers    AS c ON c.Id = a.CustomerId
    INNER JOIN dbo.Branches     AS b ON b.Id = a.BranchId
    INNER JOIN dbo.LoanProducts AS p ON p.Id = a.LoanProductId
    CROSS APPLY (SELECT DATEDIFF(HOUR, a.SubmittedAtUtc, SYSUTCDATETIME()) / 24 AS AgeDays) AS age
    WHERE a.IsDeleted = 0
      AND a.Status IN (N''Submitted'', N''PendingSecondApproval'')
      AND a.SubmittedAtUtc IS NOT NULL;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154810_AddReportingObjects'
)
BEGIN
    EXEC(N'/*
        dbo.usp_GetApprovalSummary
        ---------------------------------------------------------------------------------------------
        Approval summary for loan applications created between @FromDate and @ToDate (inclusive,
        Asia/Colombo business dates), optionally restricted to one branch.

        Result sets
          1. By status   : Status, ApplicationCount, TotalAmount
          2. By branch   : BranchId, BranchCode, BranchName, ApplicationCount, TotalAmount,
                           ApprovedCount, ApprovedAmount, RejectedCount, PendingCount
          3. By product  : ProductId, ProductCode, ProductName, ApplicationCount, TotalAmount,
                           ApprovedCount, ApprovedAmount, AverageAmount
          4. Overall     : TotalApplications, TotalAmount, ApprovedCount, RejectedCount, DecidedCount,
                           AverageTurnaroundHours, MinTurnaroundHours, MaxTurnaroundHours
                           (turnaround = first submission -> final Approved/Rejected decision)

        Called by: ReportsController (UI) and GET /api/v1/reports/summary via parameterised ADO.NET.
        Source of truth: /database/procedures. Applied by EF Core migration AddReportingObjects.
    */
    CREATE OR ALTER PROCEDURE dbo.usp_GetApprovalSummary
        @FromDate DATE,
        @ToDate   DATE,
        @BranchId INT = NULL
    AS
    BEGIN
        SET NOCOUNT ON;

        IF @FromDate IS NULL OR @ToDate IS NULL OR @ToDate < @FromDate
        BEGIN
            THROW 50001, N''@FromDate and @ToDate are required and @ToDate cannot be earlier than @FromDate.'', 1;
        END;

        -- Convert the local (Asia/Colombo) date boundaries to UTC once, so the predicates on the
        -- UTC columns remain sargable and can use IX_LoanApplications_Status_CreatedAtUtc.
        DECLARE @FromUtc DATETIME2 = CAST(CAST(@FromDate AS DATETIME2) AT TIME ZONE N''Sri Lanka Standard Time'' AT TIME ZONE N''UTC'' AS DATETIME2);
        DECLARE @ToUtc   DATETIME2 = CAST(CAST(DATEADD(DAY, 1, @ToDate) AS DATETIME2) AT TIME ZONE N''Sri Lanka Standard Time'' AT TIME ZONE N''UTC'' AS DATETIME2);

        CREATE TABLE #Scope
        (
            Id                  INT            NOT NULL PRIMARY KEY,
            Status              NVARCHAR(30)   NOT NULL,
            Amount              DECIMAL(18, 2) NOT NULL,
            BranchId            INT            NOT NULL,
            LoanProductId       INT            NOT NULL,
            FirstSubmittedAtUtc DATETIME2      NULL,
            DecidedAtUtc        DATETIME2      NULL
        );

        INSERT INTO #Scope (Id, Status, Amount, BranchId, LoanProductId, FirstSubmittedAtUtc, DecidedAtUtc)
        SELECT a.Id, a.Status, a.Amount, a.BranchId, a.LoanProductId, fs.FirstSubmittedAtUtc, a.DecidedAtUtc
        FROM dbo.LoanApplications AS a
        OUTER APPLY
        (
            SELECT MIN(x.PerformedAtUtc) AS FirstSubmittedAtUtc
            FROM dbo.ApprovalActions AS x
            WHERE x.LoanApplicationId = a.Id
              AND x.ActionType = N''Submitted''
        ) AS fs
        WHERE a.IsDeleted = 0
          AND a.CreatedAtUtc >= @FromUtc
          AND a.CreatedAtUtc <  @ToUtc
          AND (@BranchId IS NULL OR a.BranchId = @BranchId)
        OPTION (RECOMPILE); -- optional-parameter pattern: compile a plan for the actual @BranchId value

        /* 1. By status */
        SELECT s.Status,
               COUNT(*)                                AS ApplicationCount,
               CAST(SUM(s.Amount) AS DECIMAL(18, 2))   AS TotalAmount
        FROM #Scope AS s
        GROUP BY s.Status
        ORDER BY CASE s.Status
                     WHEN N''Draft'' THEN 1
                     WHEN N''Submitted'' THEN 2
                     WHEN N''PendingSecondApproval'' THEN 3
                     WHEN N''ReturnedForCorrection'' THEN 4
                     WHEN N''Approved'' THEN 5
                     WHEN N''Rejected'' THEN 6
                     ELSE 7
                 END;

        /* 2. By branch */
        SELECT b.Id                                                                              AS BranchId,
               b.Code                                                                            AS BranchCode,
               b.Name                                                                            AS BranchName,
               COUNT(*)                                                                          AS ApplicationCount,
               CAST(SUM(s.Amount) AS DECIMAL(18, 2))                                             AS TotalAmount,
               SUM(CASE WHEN s.Status = N''Approved'' THEN 1 ELSE 0 END)                           AS ApprovedCount,
               CAST(SUM(CASE WHEN s.Status = N''Approved'' THEN s.Amount ELSE 0 END) AS DECIMAL(18, 2)) AS ApprovedAmount,
               SUM(CASE WHEN s.Status = N''Rejected'' THEN 1 ELSE 0 END)                           AS RejectedCount,
               SUM(CASE WHEN s.Status IN (N''Submitted'', N''PendingSecondApproval'') THEN 1 ELSE 0 END) AS PendingCount
        FROM #Scope AS s
        INNER JOIN dbo.Branches AS b ON b.Id = s.BranchId
        GROUP BY b.Id, b.Code, b.Name
        ORDER BY b.Name;

        /* 3. By product */
        SELECT p.Id                                                                              AS ProductId,
               p.Code                                                                            AS ProductCode,
               p.Name                                                                            AS ProductName,
               COUNT(*)                                                                          AS ApplicationCount,
               CAST(SUM(s.Amount) AS DECIMAL(18, 2))                                             AS TotalAmount,
               SUM(CASE WHEN s.Status = N''Approved'' THEN 1 ELSE 0 END)                           AS ApprovedCount,
               CAST(SUM(CASE WHEN s.Status = N''Approved'' THEN s.Amount ELSE 0 END) AS DECIMAL(18, 2)) AS ApprovedAmount,
               CAST(AVG(s.Amount) AS DECIMAL(18, 2))                                             AS AverageAmount
        FROM #Scope AS s
        INNER JOIN dbo.LoanProducts AS p ON p.Id = s.LoanProductId
        GROUP BY p.Id, p.Code, p.Name
        ORDER BY p.Name;

        /* 4. Overall totals and turnaround (first submission -> final decision) */
        SELECT COUNT(*)                                                                          AS TotalApplications,
               CAST(ISNULL(SUM(s.Amount), 0) AS DECIMAL(18, 2))                                  AS TotalAmount,
               ISNULL(SUM(CASE WHEN s.Status = N''Approved'' THEN 1 ELSE 0 END), 0)                AS ApprovedCount,
               ISNULL(SUM(CASE WHEN s.Status = N''Rejected'' THEN 1 ELSE 0 END), 0)                AS RejectedCount,
               ISNULL(SUM(t.IsDecided), 0)                                                       AS DecidedCount,
               CAST(AVG(t.TurnaroundHours) AS DECIMAL(10, 2))                                    AS AverageTurnaroundHours,
               CAST(MIN(t.TurnaroundHours) AS DECIMAL(10, 2))                                    AS MinTurnaroundHours,
               CAST(MAX(t.TurnaroundHours) AS DECIMAL(10, 2))                                    AS MaxTurnaroundHours
        FROM #Scope AS s
        CROSS APPLY
        (
            SELECT CASE WHEN s.Status IN (N''Approved'', N''Rejected'') AND s.DecidedAtUtc IS NOT NULL AND s.FirstSubmittedAtUtc IS NOT NULL
                        THEN 1 ELSE 0 END AS IsDecided,
                   CASE WHEN s.Status IN (N''Approved'', N''Rejected'') AND s.DecidedAtUtc IS NOT NULL AND s.FirstSubmittedAtUtc IS NOT NULL
                        THEN DATEDIFF(MINUTE, s.FirstSubmittedAtUtc, s.DecidedAtUtc) / 60.0 END AS TurnaroundHours
        ) AS t;
    END;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154810_AddReportingObjects'
)
BEGIN
    EXEC(N'/*
        dbo.usp_GetCheckerPerformance
        ---------------------------------------------------------------------------------------------
        Decision statistics per checker for actions taken between @FromDate and @ToDate (inclusive,
        Asia/Colombo business dates).

        Columns: CheckerId, CheckerName, TotalDecisions, ApprovedCount, FirstApprovalCount,
                 RejectedCount, ReturnedCount, ApprovedAmount, AverageResponseHours
        AverageResponseHours = time from the most recent (re)submission before the decision to the decision.

        Called by: ReportsController (UI) via parameterised ADO.NET.
        Source of truth: /database/procedures. Applied by EF Core migration AddReportingObjects.
    */
    CREATE OR ALTER PROCEDURE dbo.usp_GetCheckerPerformance
        @FromDate DATE,
        @ToDate   DATE
    AS
    BEGIN
        SET NOCOUNT ON;

        IF @FromDate IS NULL OR @ToDate IS NULL OR @ToDate < @FromDate
        BEGIN
            THROW 50002, N''@FromDate and @ToDate are required and @ToDate cannot be earlier than @FromDate.'', 1;
        END;

        DECLARE @FromUtc DATETIME2 = CAST(CAST(@FromDate AS DATETIME2) AT TIME ZONE N''Sri Lanka Standard Time'' AT TIME ZONE N''UTC'' AS DATETIME2);
        DECLARE @ToUtc   DATETIME2 = CAST(CAST(DATEADD(DAY, 1, @ToDate) AS DATETIME2) AT TIME ZONE N''Sri Lanka Standard Time'' AT TIME ZONE N''UTC'' AS DATETIME2);

        WITH Decisions AS
        (
            SELECT act.PerformedById,
                   act.PerformedByName,
                   act.ActionType,
                   act.PerformedAtUtc,
                   app.Amount,
                   sub.SubmittedAtUtc
            FROM dbo.ApprovalActions AS act
            INNER JOIN dbo.LoanApplications AS app
                    ON app.Id = act.LoanApplicationId
                   AND app.IsDeleted = 0
            OUTER APPLY
            (
                SELECT MAX(s.PerformedAtUtc) AS SubmittedAtUtc
                FROM dbo.ApprovalActions AS s
                WHERE s.LoanApplicationId = act.LoanApplicationId
                  AND s.ActionType IN (N''Submitted'', N''Resubmitted'')
                  AND s.PerformedAtUtc <= act.PerformedAtUtc
            ) AS sub
            WHERE act.ActionType IN (N''FirstApproval'', N''Approved'', N''Rejected'', N''ReturnedForCorrection'')
              AND act.PerformedAtUtc >= @FromUtc
              AND act.PerformedAtUtc <  @ToUtc
        )
        SELECT d.PerformedById                                                                   AS CheckerId,
               MAX(d.PerformedByName)                                                            AS CheckerName,
               COUNT(*)                                                                          AS TotalDecisions,
               SUM(CASE WHEN d.ActionType = N''Approved'' THEN 1 ELSE 0 END)                       AS ApprovedCount,
               SUM(CASE WHEN d.ActionType = N''FirstApproval'' THEN 1 ELSE 0 END)                  AS FirstApprovalCount,
               SUM(CASE WHEN d.ActionType = N''Rejected'' THEN 1 ELSE 0 END)                       AS RejectedCount,
               SUM(CASE WHEN d.ActionType = N''ReturnedForCorrection'' THEN 1 ELSE 0 END)          AS ReturnedCount,
               CAST(SUM(CASE WHEN d.ActionType = N''Approved'' THEN d.Amount ELSE 0 END) AS DECIMAL(18, 2)) AS ApprovedAmount,
               CAST(AVG(DATEDIFF(MINUTE, d.SubmittedAtUtc, d.PerformedAtUtc) / 60.0) AS DECIMAL(10, 2))  AS AverageResponseHours
        FROM Decisions AS d
        GROUP BY d.PerformedById
        ORDER BY TotalDecisions DESC, CheckerName;
    END;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154810_AddReportingObjects'
)
BEGIN
    EXEC(N'/*
        dbo.trg_AuditLogs_AppendOnly
        ---------------------------------------------------------------------------------------------
        Database-level enforcement that the audit trail is append-only. The application has no code
        path that updates or deletes audit rows; this trigger also blocks ad-hoc UPDATE/DELETE
        statements. (TRUNCATE is not covered by triggers - deny ALTER on the table to application and
        support logins; see database/security/least-privilege-app-role.sql.)

        Source of truth: /database/triggers. Applied by EF Core migration AddReportingObjects.
    */
    CREATE OR ALTER TRIGGER dbo.trg_AuditLogs_AppendOnly
    ON dbo.AuditLogs
    INSTEAD OF UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;
        THROW 50010, N''AuditLogs is append-only: UPDATE and DELETE are not permitted.'', 1;
    END;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014154810_AddReportingObjects'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014154810_AddReportingObjects', N'10.0.13');
END;

COMMIT;
GO

