/*
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
        THROW 50002, N'@FromDate and @ToDate are required and @ToDate cannot be earlier than @FromDate.', 1;
    END;

    DECLARE @FromUtc DATETIME2 = CAST(CAST(@FromDate AS DATETIME2) AT TIME ZONE N'Sri Lanka Standard Time' AT TIME ZONE N'UTC' AS DATETIME2);
    DECLARE @ToUtc   DATETIME2 = CAST(CAST(DATEADD(DAY, 1, @ToDate) AS DATETIME2) AT TIME ZONE N'Sri Lanka Standard Time' AT TIME ZONE N'UTC' AS DATETIME2);

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
              AND s.ActionType IN (N'Submitted', N'Resubmitted')
              AND s.PerformedAtUtc <= act.PerformedAtUtc
        ) AS sub
        WHERE act.ActionType IN (N'FirstApproval', N'Approved', N'Rejected', N'ReturnedForCorrection')
          AND act.PerformedAtUtc >= @FromUtc
          AND act.PerformedAtUtc <  @ToUtc
    )
    SELECT d.PerformedById                                                                   AS CheckerId,
           MAX(d.PerformedByName)                                                            AS CheckerName,
           COUNT(*)                                                                          AS TotalDecisions,
           SUM(CASE WHEN d.ActionType = N'Approved' THEN 1 ELSE 0 END)                       AS ApprovedCount,
           SUM(CASE WHEN d.ActionType = N'FirstApproval' THEN 1 ELSE 0 END)                  AS FirstApprovalCount,
           SUM(CASE WHEN d.ActionType = N'Rejected' THEN 1 ELSE 0 END)                       AS RejectedCount,
           SUM(CASE WHEN d.ActionType = N'ReturnedForCorrection' THEN 1 ELSE 0 END)          AS ReturnedCount,
           CAST(SUM(CASE WHEN d.ActionType = N'Approved' THEN d.Amount ELSE 0 END) AS DECIMAL(18, 2)) AS ApprovedAmount,
           CAST(AVG(DATEDIFF(MINUTE, d.SubmittedAtUtc, d.PerformedAtUtc) / 60.0) AS DECIMAL(10, 2))  AS AverageResponseHours
    FROM Decisions AS d
    GROUP BY d.PerformedById
    ORDER BY TotalDecisions DESC, CheckerName;
END;
