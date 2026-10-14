using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Application.Common.Time;
using ApproveFlow.Application.DataImport;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Domain.Rules;
using ApproveFlow.Infrastructure.Persistence;
using CsvHelper;
using CsvHelper.Configuration;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ApproveFlow.Infrastructure.DataImport;

internal sealed partial class LegacyLoanImportService : ILegacyLoanImportService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<LegacyLoanImportService> _logger;

    public LegacyLoanImportService(ApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock, ILogger<LegacyLoanImportService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ImportResultDto> ImportAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var importer = _currentUser.RequireRole(AppRoles.Admin);
        var safeFileName = Path.GetFileName(fileName ?? "upload.csv");

        var bytes = await ReadAllAsync(content, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var previous = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.FileHash == hash && b.Status == ImportBatchStatus.Completed)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous.HasValue)
        {
            throw new ConflictException($"This file has already been imported (batch #{previous.Value}). Importing it again would duplicate data.");
        }

        var rows = ParseCsv(bytes);
        var context = await LoadReferenceDataAsync(rows, cancellationToken);
        var validated = rows.Select(row => Validate(row, context)).ToList();

        var now = _clock.GetUtcNow().UtcDateTime;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var batch = new ImportBatch(safeFileName, hash, now);
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);

        // New customers first, so their generated keys exist before the applications reference them.
        var customers = new Dictionary<string, Customer>(context.ExistingCustomers, StringComparer.Ordinal);
        var nextCustomerNumber = context.LastCustomerNumber;
        foreach (var row in validated.Where(v => v.IsValid && !customers.ContainsKey(v.Nic!)))
        {
            var customer = new Customer(string.Create(CultureInfo.InvariantCulture, $"CIF{++nextCustomerNumber:D6}"), row.CustomerName!, row.Nic!,
                row.Branch!.Id);
            customers[row.Nic!] = customer;
            _db.Customers.Add(customer);
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var row in validated)
        {
            if (row.IsValid)
            {
                _db.LoanApplications.Add(LoanApplication.CreateFromLegacy(row.Reference!, customers[row.Nic!], row.Product!, row.Branch!,
                    row.Terms!, row.Status, row.ApplicationDateUtc, batch.Id, importer, now));
            }
            else
            {
                batch.AddError(row.Source.LineNumber, row.Source.Get("LegacyReference"), string.Join("; ", row.Errors), row.Source.RawLine);
            }
        }

        var totalAmount = validated.Sum(v => v.ParsedAmount ?? 0m);
        var importedAmount = validated.Where(v => v.IsValid).Sum(v => v.ParsedAmount ?? 0m);
        batch.Complete(validated.Count, validated.Count(v => v.IsValid), validated.Count(v => !v.IsValid), totalAmount, importedAmount,
            totalAmount - importedAmount, _clock.GetUtcNow().UtcDateTime);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Import batch {BatchId} ({FileName}) by {UserId}: {Imported} imported, {Rejected} rejected",
            batch.Id, safeFileName, importer.UserId, batch.ImportedRows, batch.RejectedRows);

        return await GetBatchAsync(batch.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ImportBatchSummaryDto>> GetBatchesAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin, AppRoles.Auditor);
        var batches = await _db.ImportBatches.AsNoTracking().OrderByDescending(b => b.Id).Take(50).ToListAsync(cancellationToken);
        return batches.Select(b => new ImportBatchSummaryDto(b.Id, b.FileName, b.Status.ToString(), b.TotalRows, b.ImportedRows, b.RejectedRows,
            b.TotalAmountInFile, b.ImportedAmount, b.IsReconciled, b.StartedAtUtc, b.CreatedByName)).ToList();
    }

    public async Task<ImportResultDto> GetBatchAsync(int id, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin, AppRoles.Auditor);
        var batch = await _db.ImportBatches.AsNoTracking().Include(b => b.Errors).FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException("Import batch", id);

        return new ImportResultDto(batch.Id, batch.FileName, batch.Status.ToString(), batch.TotalRows, batch.ImportedRows, batch.RejectedRows,
            batch.TotalAmountInFile, batch.ImportedAmount, batch.RejectedAmount, batch.IsReconciled, batch.StartedAtUtc, batch.CompletedAtUtc,
            batch.CreatedByName,
            batch.Errors.OrderBy(e => e.RowNumber).Select(e => new ImportErrorDto(e.RowNumber, e.LegacyReference, e.Reason, e.RawData)).ToList());
    }

    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > LegacyCsvFormat.MaxFileBytes)
            {
                throw new ValidationException("File", $"The file exceeds the maximum size of {LegacyCsvFormat.MaxFileBytes / 1024 / 1024} MB.");
            }
        }

        if (buffer.Length == 0)
        {
            throw new ValidationException("File", "The file is empty.");
        }

        return buffer.ToArray();
    }

    private static List<CsvRow> ParseCsv(byte[] bytes)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
        };

        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, configuration);

        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is null)
        {
            throw new ValidationException("File", "The file has no header row.");
        }

        var header = csv.HeaderRecord.Select((name, index) => (Name: name.Trim(), Index: index))
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.OrdinalIgnoreCase);
        var missing = LegacyCsvFormat.Columns.Where(c => !header.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException("File", $"Missing required column(s): {string.Join(", ", missing)}. Expected: {string.Join(",", LegacyCsvFormat.Columns)}.");
        }

        var rows = new List<CsvRow>();
        while (csv.Read())
        {
            if (rows.Count >= LegacyCsvFormat.MaxRows)
            {
                throw new ValidationException("File", $"The file has more than {LegacyCsvFormat.MaxRows} data rows; split it into smaller batches.");
            }

            var values = header.ToDictionary(h => h.Key, h => csv.TryGetField<string>(h.Value, out var v) ? v?.Trim() : null, StringComparer.OrdinalIgnoreCase);
            rows.Add(new CsvRow(csv.Parser.Row, csv.Parser.RawRecord.TrimEnd('\r', '\n'), values));
        }

        if (rows.Count == 0)
        {
            throw new ValidationException("File", "The file contains a header but no data rows.");
        }

        return rows;
    }

    private async Task<ImportContext> LoadReferenceDataAsync(IReadOnlyCollection<CsvRow> rows, CancellationToken cancellationToken)
    {
        var references = rows.Select(r => r.Get("LegacyReference")?.ToUpperInvariant()).OfType<string>().Distinct().ToList();
        var nics = rows.Select(r => Customer.NormaliseNic(r.Get("CustomerNic"))).Where(n => n.Length > 0).Distinct().ToList();

        var existingReferences = await _db.LoanApplications.IgnoreQueryFilters()
            .Where(a => references.Contains(a.ReferenceNumber)).Select(a => a.ReferenceNumber).ToListAsync(cancellationToken);
        var customers = await _db.Customers.Where(c => nics.Contains(c.Nic)).ToListAsync(cancellationToken);
        var customerNumbers = await _db.Customers.Select(c => c.CustomerNumber).ToListAsync(cancellationToken);

        return new ImportContext(
            await _db.Branches.ToDictionaryAsync(b => b.Code, StringComparer.OrdinalIgnoreCase, cancellationToken),
            await _db.LoanProducts.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, cancellationToken),
            new HashSet<string>(existingReferences, StringComparer.OrdinalIgnoreCase),
            customers.ToDictionary(c => c.Nic, StringComparer.Ordinal),
            customerNumbers.Select(n => int.TryParse(n.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0)
                .DefaultIfEmpty(0).Max(),
            ColomboTime.Today(_clock));
    }

    /// <summary>Validates one row. Domain rules (terms, product limits, NIC format) are reused rather than duplicated.</summary>
    private static ValidatedRow Validate(CsvRow row, ImportContext context)
    {
        var errors = new List<string>();
        var result = new ValidatedRow(row, errors);

        var reference = row.Get("LegacyReference")?.ToUpperInvariant();
        if (string.IsNullOrEmpty(reference))
        {
            errors.Add("LegacyReference is required");
        }
        else if (!LegacyReferencePattern().IsMatch(reference) || reference.StartsWith(LoanPolicy.ReferencePrefix + "-", StringComparison.Ordinal))
        {
            errors.Add($"LegacyReference '{reference}' is not a valid legacy reference (e.g. LEG-2019-00045; the LDB- prefix is reserved)");
        }
        else if (context.ExistingReferences.Contains(reference))
        {
            errors.Add($"Duplicate reference: {reference} already exists in ApproveFlow");
        }
        else if (!context.SeenReferences.Add(reference))
        {
            errors.Add($"Duplicate reference: {reference} appears more than once in this file");
        }

        result.Reference = reference;

        var nic = Customer.NormaliseNic(row.Get("CustomerNic"));
        if (!Customer.IsValidNic(nic))
        {
            errors.Add("CustomerNic must be 9 digits followed by V/X, or 12 digits");
        }
        else if (context.ExistingCustomers.TryGetValue(nic, out var existing))
        {
            if (!existing.IsActive)
            {
                errors.Add($"Customer with NIC {nic} is inactive");
            }
        }
        else if (string.IsNullOrWhiteSpace(row.Get("CustomerName")))
        {
            errors.Add("CustomerName is required for a customer not yet in ApproveFlow");
        }
        else if (row.Get("CustomerName")!.Length > 150)
        {
            errors.Add("CustomerName cannot exceed 150 characters");
        }

        result.Nic = nic;
        result.CustomerName = row.Get("CustomerName");

        var branchCode = row.Get("BranchCode");
        if (string.IsNullOrEmpty(branchCode) || !context.Branches.TryGetValue(branchCode, out var branch))
        {
            errors.Add($"Unknown branch code '{branchCode}'");
        }
        else if (!branch.IsActive)
        {
            errors.Add($"Branch '{branchCode}' is inactive");
        }
        else
        {
            result.Branch = branch;
        }

        var productCode = row.Get("ProductCode");
        if (string.IsNullOrEmpty(productCode) || !context.Products.TryGetValue(productCode, out var product))
        {
            errors.Add($"Unknown product code '{productCode}'");
        }
        else
        {
            result.Product = product;
        }

        var amount = ParseDecimal(row, "Amount", errors);
        result.ParsedAmount = amount;
        var tenure = ParseInt(row, "TenureMonths", errors);
        var rate = ParseDecimal(row, "InterestRate", errors);
        var income = ParseDecimal(row, "MonthlyIncome", errors);
        var obligations = string.IsNullOrEmpty(row.Get("ExistingObligations")) ? 0m : ParseDecimal(row, "ExistingObligations", errors);

        var statusText = row.Get("Status");
        var status = LegacyCsvFormat.AllowedStatuses.FirstOrDefault(s => string.Equals(s, statusText, StringComparison.OrdinalIgnoreCase));
        if (status is null)
        {
            errors.Add($"Status '{statusText}' is not allowed (use {string.Join(", ", LegacyCsvFormat.AllowedStatuses)})");
        }
        else
        {
            result.Status = Enum.Parse<LoanStatus>(status);
        }

        if (!DateOnly.TryParseExact(row.Get("ApplicationDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add($"ApplicationDate '{row.Get("ApplicationDate")}' must be a date in yyyy-MM-dd format");
        }
        else if (date > context.Today || date < new DateOnly(2000, 1, 1))
        {
            errors.Add($"ApplicationDate {date:yyyy-MM-dd} must be between 2000-01-01 and today");
        }
        else
        {
            result.ApplicationDateUtc = ColomboTime.StartOfDayUtc(date).AddHours(10);
        }

        if (amount.HasValue && tenure.HasValue && rate.HasValue && income.HasValue && obligations.HasValue)
        {
            try
            {
                result.Terms = new LoanTerms(amount.Value, tenure.Value, rate.Value, row.Get("Purpose") ?? string.Empty, income.Value, obligations.Value);
                result.Product?.EnsureWithinLimits(amount.Value, tenure.Value);
            }
            catch (BusinessRuleViolationException ex)
            {
                errors.Add(ex.Message.TrimEnd('.'));
            }
        }

        return result;
    }

    private static decimal? ParseDecimal(CsvRow row, string column, List<string> errors)
    {
        var text = row.Get(column);
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add(string.IsNullOrEmpty(text) ? $"{column} is required" : $"{column} '{text}' is not a valid number");
        return null;
    }

    private static int? ParseInt(CsvRow row, string column, List<string> errors)
    {
        var text = row.Get(column);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add(string.IsNullOrEmpty(text) ? $"{column} is required" : $"{column} '{text}' is not a whole number");
        return null;
    }

    [GeneratedRegex(@"^[A-Z]{2,5}-\d{4}-\d{3,8}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex LegacyReferencePattern();

    private sealed record CsvRow(int LineNumber, string RawLine, IReadOnlyDictionary<string, string?> Values)
    {
        public string? Get(string column) => Values.TryGetValue(column, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }

    private sealed record ImportContext(
        IReadOnlyDictionary<string, Branch> Branches,
        IReadOnlyDictionary<string, LoanProduct> Products,
        HashSet<string> ExistingReferences,
        IReadOnlyDictionary<string, Customer> ExistingCustomers,
        int LastCustomerNumber,
        DateOnly Today)
    {
        public HashSet<string> SeenReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ValidatedRow
    {
        public ValidatedRow(CsvRow source, List<string> errors)
        {
            Source = source;
            Errors = errors;
        }

        public CsvRow Source { get; }
        public List<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
        public string? Reference { get; set; }
        public string? Nic { get; set; }
        public string? CustomerName { get; set; }
        public Branch? Branch { get; set; }
        public LoanProduct? Product { get; set; }
        public LoanTerms? Terms { get; set; }
        public LoanStatus Status { get; set; }
        public DateTime ApplicationDateUtc { get; set; }
        public decimal? ParsedAmount { get; set; }
    }
}
