/*
    Least-privilege database access for the ApproveFlow web application (run by a DBA; not applied by migrations).

    Model: schema changes are applied by a separate deployment login (migration bundle / idempotent script).
    The runtime login used in ConnectionStrings:DefaultConnection only reads and writes data:
      * no DDL (cannot ALTER/DROP tables, so it cannot TRUNCATE or disable the audit trigger)
      * cannot UPDATE or DELETE audit rows (DENY, in addition to the append-only trigger)
      * may execute the reporting procedures
    Replace <app_user> with the database user mapped to the application login.
*/
IF DATABASE_PRINCIPAL_ID(N'approveflow_app') IS NULL
BEGIN
    CREATE ROLE approveflow_app;
END;

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO approveflow_app;
GRANT EXECUTE ON OBJECT::dbo.usp_GetApprovalSummary TO approveflow_app;
GRANT EXECUTE ON OBJECT::dbo.usp_GetCheckerPerformance TO approveflow_app;
GRANT SELECT ON OBJECT::dbo.vw_PendingApprovalsAging TO approveflow_app;
-- Sequences are schema objects: NEXT VALUE FOR needs UPDATE on the sequence.
GRANT UPDATE ON OBJECT::dbo.LoanApplicationNumbers TO approveflow_app;

-- The audit trail is append-only for the application.
DENY UPDATE, DELETE ON OBJECT::dbo.AuditLogs TO approveflow_app;
DENY ALTER ON OBJECT::dbo.AuditLogs TO approveflow_app;

-- Example: ALTER ROLE approveflow_app ADD MEMBER [<app_user>];
