using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Domain.Entities;

/// <summary>One upload of legacy loan data, with reconciliation totals.</summary>
public class ImportBatch : AuditableEntity
{
    private readonly List<ImportError> _errors = new();

    private ImportBatch()
    {
    }

    public ImportBatch(string fileName, string fileHash, DateTime startedAtUtc)
    {
        FileName = Guard.Required(fileName, nameof(fileName));
        FileHash = Guard.Required(fileHash, nameof(fileHash));
        StartedAtUtc = startedAtUtc;
        Status = ImportBatchStatus.Completed;
    }

    public string FileName { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the uploaded file, used to stop the same file being imported twice.</summary>
    public string FileHash { get; private set; } = string.Empty;

    public ImportBatchStatus Status { get; private set; }
    public int TotalRows { get; private set; }
    public int ImportedRows { get; private set; }
    public int RejectedRows { get; private set; }

    /// <summary>Sum of every amount in the file that could be parsed as a number.</summary>
    public decimal TotalAmountInFile { get; private set; }

    public decimal ImportedAmount { get; private set; }
    public decimal RejectedAmount { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<ImportError> Errors => _errors.AsReadOnly();

    /// <summary>Row counts and amounts both balance: imported + rejected = total.</summary>
    public bool IsReconciled =>
        ImportedRows + RejectedRows == TotalRows && ImportedAmount + RejectedAmount == TotalAmountInFile;

    public void AddError(int rowNumber, string? legacyReference, string reason, string? rawData)
    {
        _errors.Add(new ImportError(this, rowNumber, legacyReference, reason, rawData));
    }

    public void Complete(int totalRows, int importedRows, int rejectedRows, decimal totalAmountInFile, decimal importedAmount,
        decimal rejectedAmount, DateTime completedAtUtc)
    {
        TotalRows = totalRows;
        ImportedRows = importedRows;
        RejectedRows = rejectedRows;
        TotalAmountInFile = totalAmountInFile;
        ImportedAmount = importedAmount;
        RejectedAmount = rejectedAmount;
        CompletedAtUtc = completedAtUtc;
        Status = ImportBatchStatus.Completed;
    }
}

public class ImportError : Entity
{
    private ImportError()
    {
    }

    internal ImportError(ImportBatch batch, int rowNumber, string? legacyReference, string reason, string? rawData)
    {
        ImportBatch = batch;
        RowNumber = rowNumber;
        LegacyReference = Truncate(legacyReference, 50);
        Reason = Truncate(reason, 1000) ?? string.Empty;
        RawData = Truncate(rawData, 2000);
    }

    public int ImportBatchId { get; private set; }
    public ImportBatch? ImportBatch { get; private set; }

    /// <summary>1-based line number in the file (the header is line 1).</summary>
    public int RowNumber { get; private set; }

    public string? LegacyReference { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? RawData { get; private set; }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
