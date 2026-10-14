using ApproveFlow.Application.Dashboard;
using ApproveFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Persistence.Queries;

internal sealed class DashboardQueries : IDashboardQueries
{
    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public DashboardQueries(ApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<DashboardCounts> GetCountsAsync(string userId, DateTime todayStartUtc, DateTime tomorrowStartUtc,
        CancellationToken cancellationToken = default)
    {
        var applications = _db.LoanApplications.AsNoTracking();

        var byStatus = (await applications.GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Status, x => x.Count);

        var mine = (await applications.Where(a => a.CreatedById == userId)
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Status, x => x.Count);

        var decidedToday = await applications
            .Where(a => a.DecidedAtUtc >= todayStartUtc && a.DecidedAtUtc < tomorrowStartUtc
                        && (a.Status == LoanStatus.Approved || a.Status == LoanStatus.Rejected))
            .Select(a => new { a.Status, a.Amount })
            .ToListAsync(cancellationToken);

        int Get(IReadOnlyDictionary<LoanStatus, int> source, LoanStatus status) => source.TryGetValue(status, out var n) ? n : 0;

        return new DashboardCounts(
            MyDrafts: Get(mine, LoanStatus.Draft),
            MyReturned: Get(mine, LoanStatus.ReturnedForCorrection),
            MyPendingDecision: Get(mine, LoanStatus.Submitted) + Get(mine, LoanStatus.PendingSecondApproval),
            PendingApprovals: Get(byStatus, LoanStatus.Submitted) + Get(byStatus, LoanStatus.PendingSecondApproval),
            AwaitingSecondApproval: Get(byStatus, LoanStatus.PendingSecondApproval),
            ApprovedToday: decidedToday.Count(d => d.Status == LoanStatus.Approved),
            RejectedToday: decidedToday.Count(d => d.Status == LoanStatus.Rejected),
            ApprovedTodayAmount: decidedToday.Where(d => d.Status == LoanStatus.Approved).Sum(d => d.Amount),
            ByStatus: byStatus);
    }

    public async Task<IReadOnlyList<AgingBucketDto>> GetPendingAgingAsync(CancellationToken cancellationToken = default)
    {
        if (_db.IsSqlServer)
        {
            // Reads the database view dbo.vw_PendingApprovalsAging (static SQL, no user input).
            return await _db.Database.SqlQuery<AgingBucketDto>($"""
                SELECT AgeBucket, AgeBucketOrder, COUNT(*) AS ApplicationCount, CAST(SUM(Amount) AS DECIMAL(18, 2)) AS TotalAmount
                FROM dbo.vw_PendingApprovalsAging
                GROUP BY AgeBucket, AgeBucketOrder
                ORDER BY AgeBucketOrder
                """).ToListAsync(cancellationToken);
        }

        // Portable equivalent of the view for SQLite (tests and demo mode).
        var now = _clock.GetUtcNow().UtcDateTime;
        var pending = await _db.LoanApplications.AsNoTracking()
            .Where(a => (a.Status == LoanStatus.Submitted || a.Status == LoanStatus.PendingSecondApproval) && a.SubmittedAtUtc != null)
            .Select(a => new { a.SubmittedAtUtc, a.Amount })
            .ToListAsync(cancellationToken);

        static int BucketFor(int ageDays) => ageDays <= 2 ? 1 : ageDays <= 5 ? 2 : 3;

        return pending
            .Select(p => (Bucket: BucketFor((int)((now - p.SubmittedAtUtc!.Value).TotalHours / 24)), p.Amount))
            .GroupBy(x => x.Bucket)
            .OrderBy(g => g.Key)
            .Select(g => new AgingBucketDto(g.Key switch { 1 => "0-2 days", 2 => "3-5 days", _ => "6+ days" }, g.Key, g.Count(), g.Sum(x => x.Amount)))
            .ToList();
    }

    public async Task<IReadOnlyList<RecentActivityDto>> GetRecentActivityAsync(int take, CancellationToken cancellationToken = default) =>
        await _db.ApprovalActions.AsNoTracking()
            .OrderByDescending(a => a.PerformedAtUtc).ThenByDescending(a => a.Id)
            .Take(take)
            .Select(a => new RecentActivityDto(a.LoanApplicationId, a.LoanApplication!.ReferenceNumber, a.ActionType, a.PerformedByName,
                a.PerformedAtUtc))
            .ToListAsync(cancellationToken);
}
