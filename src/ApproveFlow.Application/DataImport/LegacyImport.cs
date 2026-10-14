namespace ApproveFlow.Application.DataImport;

/// <summary>Contract of the legacy loan CSV file accepted by the data-migration module.</summary>
public static class LegacyCsvFormat
{
    public const long MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxRows = 5_000;

    /// <summary>Required header columns (order does not matter, matching is case-insensitive).</summary>
    public static readonly IReadOnlyList<string> Columns = new[]
    {
        "LegacyReference", "CustomerNic", "CustomerName", "BranchCode", "ProductCode", "Amount", "TenureMonths", "InterestRate",
        "MonthlyIncome", "ExistingObligations", "Purpose", "Status", "ApplicationDate",
    };

    /// <summary>Statuses a legacy application may be migrated in.</summary>
    public static readonly IReadOnlyList<string> AllowedStatuses = new[] { "Approved", "Rejected", "Submitted" };
}

public sealed record ImportErrorDto(int RowNumber, string? LegacyReference, string Reason, string? RawData);

/// <summary>Reconciliation of one import: rows and amounts in the file versus imported and rejected.</summary>
public sealed record ImportResultDto(
    int BatchId,
    string FileName,
    string Status,
    int TotalRows,
    int ImportedRows,
    int RejectedRows,
    decimal TotalAmountInFile,
    decimal ImportedAmount,
    decimal RejectedAmount,
    bool IsReconciled,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string ImportedByName,
    IReadOnlyList<ImportErrorDto> Errors);

public sealed record ImportBatchSummaryDto(
    int Id,
    string FileName,
    string Status,
    int TotalRows,
    int ImportedRows,
    int RejectedRows,
    decimal TotalAmountInFile,
    decimal ImportedAmount,
    bool IsReconciled,
    DateTime StartedAtUtc,
    string ImportedByName);

/// <summary>
/// Migrates legacy loan applications from CSV. Every row is validated; valid rows are imported and invalid rows are
/// recorded with their line number and reasons - all inside one database transaction per batch.
/// </summary>
public interface ILegacyLoanImportService
{
    Task<ImportResultDto> ImportAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportBatchSummaryDto>> GetBatchesAsync(CancellationToken cancellationToken = default);
    Task<ImportResultDto> GetBatchAsync(int id, CancellationToken cancellationToken = default);
}
