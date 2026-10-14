/*
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
    THROW 50010, N'AuditLogs is append-only: UPDATE and DELETE are not permitted.', 1;
END;
