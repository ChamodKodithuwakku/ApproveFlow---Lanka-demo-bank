/*
    dbo.vw_PendingApprovalsAging
    ---------------------------------------------------------------------------------------------
    Applications awaiting a checker decision (Submitted or PendingSecondApproval) with their age
    since the latest submission and an ageing bucket: '0-2 days', '3-5 days', '6+ days'.
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
       CASE WHEN age.AgeDays <= 2 THEN N'0-2 days'
            WHEN age.AgeDays <= 5 THEN N'3-5 days'
            ELSE N'6+ days'
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
  AND a.Status IN (N'Submitted', N'PendingSecondApproval')
  AND a.SubmittedAtUtc IS NOT NULL;
