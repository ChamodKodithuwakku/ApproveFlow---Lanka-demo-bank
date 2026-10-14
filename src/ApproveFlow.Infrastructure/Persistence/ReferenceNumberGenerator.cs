using System.Globalization;
using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Time;
using ApproveFlow.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Persistence;

/// <summary>
/// Produces LDB-YYYY-NNNNNN references. On SQL Server the number comes from a SEQUENCE, which is atomic across
/// concurrent requests without locking the LoanApplications table. YYYY is the Asia/Colombo calendar year.
/// </summary>
internal sealed class ReferenceNumberGenerator : IReferenceNumberGenerator
{
    private readonly ApplicationDbContext _db;

    public ReferenceNumberGenerator(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<string> NextAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var year = ColomboTime.FromUtc(nowUtc).Year;
        long next;

        if (_db.IsSqlServer)
        {
            // Executed as-is (not composed): NEXT VALUE FOR is not allowed inside a derived table.
            var values = await _db.Database
                .SqlQuery<long>($"SELECT NEXT VALUE FOR dbo.LoanApplicationNumbers AS [Value]")
                .ToListAsync(cancellationToken);
            next = values.Single();
        }
        else
        {
            // Portable fallback for SQLite (tests / demo mode): highest existing number + 1, including tracked
            // (not yet saved) applications so several drafts can be created in one unit of work.
            var prefix = LoanPolicy.ReferencePrefix + "-";
            var stored = await _db.LoanApplications.IgnoreQueryFilters()
                .Where(a => a.ReferenceNumber.StartsWith(prefix))
                .Select(a => a.ReferenceNumber)
                .ToListAsync(cancellationToken);
            var tracked = _db.ChangeTracker.Entries<Domain.Entities.LoanApplication>().Select(e => e.Entity.ReferenceNumber);
            next = stored.Concat(tracked).Select(ParseSequence).DefaultIfEmpty(0).Max() + 1;
        }

        return LoanPolicy.FormatReference(year, next);
    }

    private static long ParseSequence(string reference)
    {
        var lastDash = reference.LastIndexOf('-');
        return lastDash >= 0 && long.TryParse(reference.AsSpan(lastDash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
