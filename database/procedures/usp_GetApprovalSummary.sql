/*
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
        THROW 50001, N'@FromDate and @ToDate are required and @ToDate cannot be earlier than @FromDate.', 1;
    END;

    -- Convert the local (Asia/Colombo) date boundaries to UTC once, so the predicates on the
    -- UTC columns remain sargable and can use IX_LoanApplications_Status_CreatedAtUtc.
    DECLARE @FromUtc DATETIME2 = CAST(CAST(@FromDate AS DATETIME2) AT TIME ZONE N'Sri Lanka Standard Time' AT TIME ZONE N'UTC' AS DATETIME2);
    DECLARE @ToUtc   DATETIME2 = CAST(CAST(DATEADD(DAY, 1, @ToDate) AS DATETIME2) AT TIME ZONE N'Sri Lanka Standard Time' AT TIME ZONE N'UTC' AS DATETIME2);

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
          AND x.ActionType = N'Submitted'
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
                 WHEN N'Draft' THEN 1
                 WHEN N'Submitted' THEN 2
                 WHEN N'PendingSecondApproval' THEN 3
                 WHEN N'ReturnedForCorrection' THEN 4
                 WHEN N'Approved' THEN 5
                 WHEN N'Rejected' THEN 6
                 ELSE 7
             END;

    /* 2. By branch */
    SELECT b.Id                                                                              AS BranchId,
           b.Code                                                                            AS BranchCode,
           b.Name                                                                            AS BranchName,
           COUNT(*)                                                                          AS ApplicationCount,
           CAST(SUM(s.Amount) AS DECIMAL(18, 2))                                             AS TotalAmount,
           SUM(CASE WHEN s.Status = N'Approved' THEN 1 ELSE 0 END)                           AS ApprovedCount,
           CAST(SUM(CASE WHEN s.Status = N'Approved' THEN s.Amount ELSE 0 END) AS DECIMAL(18, 2)) AS ApprovedAmount,
           SUM(CASE WHEN s.Status = N'Rejected' THEN 1 ELSE 0 END)                           AS RejectedCount,
           SUM(CASE WHEN s.Status IN (N'Submitted', N'PendingSecondApproval') THEN 1 ELSE 0 END) AS PendingCount
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
           SUM(CASE WHEN s.Status = N'Approved' THEN 1 ELSE 0 END)                           AS ApprovedCount,
           CAST(SUM(CASE WHEN s.Status = N'Approved' THEN s.Amount ELSE 0 END) AS DECIMAL(18, 2)) AS ApprovedAmount,
           CAST(AVG(s.Amount) AS DECIMAL(18, 2))                                             AS AverageAmount
    FROM #Scope AS s
    INNER JOIN dbo.LoanProducts AS p ON p.Id = s.LoanProductId
    GROUP BY p.Id, p.Code, p.Name
    ORDER BY p.Name;

    /* 4. Overall totals and turnaround (first submission -> final decision) */
    SELECT COUNT(*)                                                                          AS TotalApplications,
           CAST(ISNULL(SUM(s.Amount), 0) AS DECIMAL(18, 2))                                  AS TotalAmount,
           ISNULL(SUM(CASE WHEN s.Status = N'Approved' THEN 1 ELSE 0 END), 0)                AS ApprovedCount,
           ISNULL(SUM(CASE WHEN s.Status = N'Rejected' THEN 1 ELSE 0 END), 0)                AS RejectedCount,
           ISNULL(SUM(t.IsDecided), 0)                                                       AS DecidedCount,
           CAST(AVG(t.TurnaroundHours) AS DECIMAL(10, 2))                                    AS AverageTurnaroundHours,
           CAST(MIN(t.TurnaroundHours) AS DECIMAL(10, 2))                                    AS MinTurnaroundHours,
           CAST(MAX(t.TurnaroundHours) AS DECIMAL(10, 2))                                    AS MaxTurnaroundHours
    FROM #Scope AS s
    CROSS APPLY
    (
        SELECT CASE WHEN s.Status IN (N'Approved', N'Rejected') AND s.DecidedAtUtc IS NOT NULL AND s.FirstSubmittedAtUtc IS NOT NULL
                    THEN 1 ELSE 0 END AS IsDecided,
               CASE WHEN s.Status IN (N'Approved', N'Rejected') AND s.DecidedAtUtc IS NOT NULL AND s.FirstSubmittedAtUtc IS NOT NULL
                    THEN DATEDIFF(MINUTE, s.FirstSubmittedAtUtc, s.DecidedAtUtc) / 60.0 END AS TurnaroundHours
    ) AS t;
END;
