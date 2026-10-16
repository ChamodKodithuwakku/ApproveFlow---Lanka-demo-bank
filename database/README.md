# Database (SQL Server)

The schema is owned by EF Core code-first migrations in `src/ApproveFlow.Infrastructure/Persistence/Migrations`.
Objects that are not part of the EF model live here as plain T-SQL and are **embedded into the migration
`AddReportingObjects`**, so each object has exactly one source of truth.

| Folder | Contents | Applied by |
|---|---|---|
| `procedures/` | `usp_GetApprovalSummary`, `usp_GetCheckerPerformance` | Migration `AddReportingObjects` |
| `views/` | `vw_PendingApprovalsAging` (0-2 / 3-5 / 6+ day buckets) | Migration `AddReportingObjects` |
| `triggers/` | `trg_AuditLogs_AppendOnly` (blocks UPDATE/DELETE on the audit trail) | Migration `AddReportingObjects` |
| `schema/ApproveFlow_Schema_Idempotent.sql` | Full idempotent script of all migrations (tables, keys, FKs, indexes, check constraints, sequence, the objects above) | DBA, for review or manual deployment |
| `security/least-privilege-app-role.sql` | Runtime role: data access only, no DDL, `DENY UPDATE/DELETE` on `AuditLogs` | DBA |
| `seed/reference-data.sql` | Branches and loan products for production (idempotent `MERGE`) | DBA |

## Design notes

- **Indexes**: unique `ReferenceNumber`; `(Status, CreatedAtUtc)` and `(Status, SubmittedAtUtc)` for work queues
  and ageing; `CustomerId`, `CreatedById`; `ApprovalActions (LoanApplicationId, PerformedAtUtc)`,
  `(PerformedById, PerformedAtUtc)`, `(ActionType, PerformedAtUtc)` for history and checker reports; `AuditLogs`
  by time, table+key, user and event; filtered unique index on `ImportBatches.FileHash WHERE Status = 'Completed'`.
- **Constraints**: `CK_LoanApplications_Amount ([Amount] > 0)`, `CK_LoanApplications_Tenure (1-360)`, interest
  rate 0-100, income > 0, obligations >= 0, an allowed-values list for `Status`, product range checks, and
  `ApprovalLimit >= 0`. All domain foreign keys are `ON DELETE NO ACTION` (business records are never cascade-deleted).
- **Concurrency**: `LoanApplications.RowVersion` is a native `rowversion`.
- **Reference numbers**: `SEQUENCE dbo.LoanApplicationNumbers` gives contention-free, gap-tolerant numbering
  (`LDB-YYYY-000001`).
- **Time zone**: all columns are UTC (`datetime2`). Procedures convert Asia/Colombo report dates to UTC once with
  `AT TIME ZONE 'Sri Lanka Standard Time'`, keeping predicates sargable.
- The optional `@BranchId` parameter uses `OPTION (RECOMPILE)` to avoid a poor cached plan for the catch-all
  predicate.

## Regenerating the idempotent script

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations script --idempotent --project src/ApproveFlow.Infrastructure --startup-project src/ApproveFlow.Infrastructure -o database/schema/ApproveFlow_Schema_Idempotent.sql
```

Every `.sql` file in this folder is parsed with the SQL Server 2022 grammar (`Microsoft.SqlServer.TransactSql.ScriptDom`)
by the test suite, so syntax errors fail CI even though CI runs without SQL Server.
