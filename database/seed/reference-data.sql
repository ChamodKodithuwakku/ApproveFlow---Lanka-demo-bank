/*
    Production reference data: branches and loan products (idempotent MERGE; safe to re-run).
    Demo users, customers and applications are seeded by the application in Development only and never in Production.
    Audit stamps use 'dba-script' so these rows are distinguishable from application changes.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

MERGE dbo.Branches AS target
USING (VALUES
    (N'COL', N'Colombo Main Branch', N'Colombo'),
    (N'KDY', N'Kandy City Branch',   N'Kandy'),
    (N'GLE', N'Galle Fort Branch',   N'Galle')
) AS source (Code, Name, City)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.City <> source.City) THEN
    UPDATE SET Name = source.Name, City = source.City, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedById = N'dba-script', UpdatedByName = N'dba-script'
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, City, IsActive, CreatedAtUtc, CreatedById, CreatedByName)
    VALUES (source.Code, source.Name, source.City, 1, SYSUTCDATETIME(), N'dba-script', N'dba-script');

MERGE dbo.LoanProducts AS target
USING (VALUES
    (N'PL',  N'Personal Loan',     N'Unsecured personal financing for salaried customers.',                    CAST(50000 AS DECIMAL(18, 2)),   CAST(5000000 AS DECIMAL(18, 2)),  6,  84, CAST(14.50 AS DECIMAL(5, 2))),
    (N'HL',  N'Housing Loan',      N'Purchase, construction or renovation of residential property.',          CAST(1000000 AS DECIMAL(18, 2)), CAST(50000000 AS DECIMAL(18, 2)), 12, 300, CAST(11.50 AS DECIMAL(5, 2))),
    (N'VL',  N'Vehicle Loan',      N'Financing for registered and brand-new vehicles.',                        CAST(300000 AS DECIMAL(18, 2)),  CAST(15000000 AS DECIMAL(18, 2)), 12,  84, CAST(13.00 AS DECIMAL(5, 2))),
    (N'SME', N'SME Business Loan', N'Working capital and capital expenditure for small and medium enterprises.', CAST(250000 AS DECIMAL(18, 2)),  CAST(25000000 AS DECIMAL(18, 2)),  6, 120, CAST(12.75 AS DECIMAL(5, 2)))
) AS source (Code, Name, Description, MinAmount, MaxAmount, MinTenureMonths, MaxTenureMonths, DefaultInterestRate)
ON target.Code = source.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Description, MinAmount, MaxAmount, MinTenureMonths, MaxTenureMonths, DefaultInterestRate, IsActive,
            CreatedAtUtc, CreatedById, CreatedByName)
    VALUES (source.Code, source.Name, source.Description, source.MinAmount, source.MaxAmount, source.MinTenureMonths,
            source.MaxTenureMonths, source.DefaultInterestRate, 1, SYSUTCDATETIME(), N'dba-script', N'dba-script');

COMMIT TRANSACTION;
