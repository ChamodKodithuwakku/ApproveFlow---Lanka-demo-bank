using System.Text;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.DataImport;
using ApproveFlow.Web.Controllers;
using ApproveFlow.Web.Infrastructure;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Web.Areas.Admin.Controllers;

/// <summary>Legacy data migration: CSV upload, row validation, transactional import and reconciliation.</summary>
[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class DataImportController : AppController
{
    private readonly ILegacyLoanImportService _imports;

    public DataImportController(ILegacyLoanImportService imports)
    {
        _imports = imports;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await _imports.GetBatchesAsync(cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(LegacyCsvFormat.MaxFileBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = LegacyCsvFormat.MaxFileBytes + 64 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            FlashError("Choose a CSV file to upload.");
            return RedirectToAction(nameof(Index));
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            FlashError("Only .csv files are accepted.");
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > LegacyCsvFormat.MaxFileBytes)
        {
            FlashError("The file exceeds the 2 MB limit. Split it into smaller batches.");
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _imports.ImportAsync(stream, file.FileName, cancellationToken);
            FlashSuccess($"Batch #{result.BatchId} processed: {result.ImportedRows} imported, {result.RejectedRows} rejected.");
            return RedirectToAction(nameof(Batch), new { id = result.BatchId });
        }
        catch (ValidationException ex)
        {
            FlashError(string.Join(" ", ex.Errors.SelectMany(e => e.Value)));
        }
        catch (ConflictException ex)
        {
            FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Batch(int id, CancellationToken cancellationToken)
    {
        try
        {
            return View(await _imports.GetBatchAsync(id, cancellationToken));
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Downloads a CSV template with the expected header and two example rows.</summary>
    [HttpGet]
    public IActionResult Template()
    {
        var csv = new StringBuilder()
            .AppendLine(string.Join(",", LegacyCsvFormat.Columns))
            .AppendLine(Csv.Row("LEG-2021-00001", "198512345678", "Example Customer", "COL", "PL", "750000.00", "36", "15.50", "185000", "12000",
                "Purchase of household furniture", "Approved", "2021-03-15"))
            .AppendLine(Csv.Row("LEG-2022-00002", "851234567V", "Another Example", "KDY", "VL", "2400000.00", "60", "13.25", "320000", "0",
                "Purchase of a registered used car", "Rejected", "2022-07-01"))
            .ToString();
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "approveflow-legacy-template.csv");
    }
}
