namespace ApproveFlow.Infrastructure.Persistence;

/// <summary>Reads the T-SQL scripts from /database that are embedded into this assembly at build time.</summary>
public static class SqlScripts
{
    public const string ApprovalSummaryProcedure = "usp_GetApprovalSummary.sql";
    public const string CheckerPerformanceProcedure = "usp_GetCheckerPerformance.sql";
    public const string PendingAgingView = "vw_PendingApprovalsAging.sql";
    public const string AuditAppendOnlyTrigger = "trg_AuditLogs_AppendOnly.sql";

    /// <summary>
    /// Returns the script wrapped in EXEC(N'...'). CREATE VIEW/PROCEDURE/TRIGGER must be the first statement in a
    /// batch; idempotent migration scripts wrap every migration in IF NOT EXISTS(...) BEGIN ... END, so the DDL has to
    /// run as dynamic SQL to be valid in both `database update` and `migrations script --idempotent` output.
    /// </summary>
    public static string ReadAsDynamicSql(string fileName) =>
        "EXEC(N'" + Read(fileName).Replace("'", "''", StringComparison.Ordinal) + "');";

    public static string Read(string fileName)
    {
        var resourceName = "ApproveFlow.Sql." + fileName;
        using var stream = typeof(SqlScripts).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
