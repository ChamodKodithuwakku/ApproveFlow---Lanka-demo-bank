using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the SQL Server objects that live outside the EF model: reporting stored procedures, the pending-ageing
    /// view and the append-only trigger on AuditLogs. The scripts are the files in /database (embedded at build time),
    /// so the repository has a single source of truth for each object. Each script is CREATE OR ALTER and therefore
    /// idempotent.
    /// </summary>
    public partial class AddReportingObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlScripts.ReadAsDynamicSql(SqlScripts.PendingAgingView));
            migrationBuilder.Sql(SqlScripts.ReadAsDynamicSql(SqlScripts.ApprovalSummaryProcedure));
            migrationBuilder.Sql(SqlScripts.ReadAsDynamicSql(SqlScripts.CheckerPerformanceProcedure));
            migrationBuilder.Sql(SqlScripts.ReadAsDynamicSql(SqlScripts.AuditAppendOnlyTrigger));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.trg_AuditLogs_AppendOnly;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GetCheckerPerformance;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GetApprovalSummary;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_PendingApprovalsAging;");
        }
    }
}
