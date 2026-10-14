using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Time;
using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Application.Dashboard;

/// <summary>Read-model queries behind the dashboard (implemented in Infrastructure).</summary>
public interface IDashboardQueries
{
    Task<DashboardCounts> GetCountsAsync(string userId, DateTime todayStartUtc, DateTime tomorrowStartUtc, CancellationToken cancellationToken = default);

    /// <summary>Pending items grouped into 0-2 / 3-5 / 6+ day buckets (SQL Server: dbo.vw_PendingApprovalsAging).</summary>
    Task<IReadOnlyList<AgingBucketDto>> GetPendingAgingAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentActivityDto>> GetRecentActivityAsync(int take, CancellationToken cancellationToken = default);
}

public sealed record DashboardCounts(
    int MyDrafts,
    int MyReturned,
    int MyPendingDecision,
    int PendingApprovals,
    int AwaitingSecondApproval,
    int ApprovedToday,
    int RejectedToday,
    decimal ApprovedTodayAmount,
    IReadOnlyDictionary<LoanStatus, int> ByStatus);

public sealed record AgingBucketDto(string AgeBucket, int AgeBucketOrder, int ApplicationCount, decimal TotalAmount);

public sealed record RecentActivityDto(int LoanApplicationId, string ReferenceNumber, ApprovalActionType ActionType, string PerformedByName,
    DateTime PerformedAtUtc);

public sealed record DashboardDto(DashboardCounts Counts, IReadOnlyList<AgingBucketDto> Aging, IReadOnlyList<RecentActivityDto> RecentActivity);

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default);
}

internal sealed class DashboardService : IDashboardService
{
    private static readonly string[] Buckets = { "0-2 days", "3-5 days", "6+ days" };

    private readonly IDashboardQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public DashboardService(IDashboardQueries queries, ICurrentUser currentUser, TimeProvider clock)
    {
        _queries = queries;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.RequireActor();
        var today = ColomboTime.Today(_clock);
        var counts = await _queries.GetCountsAsync(actor.UserId, ColomboTime.StartOfDayUtc(today), ColomboTime.StartOfDayUtc(today.AddDays(1)),
            cancellationToken);
        var aging = await _queries.GetPendingAgingAsync(cancellationToken);
        var recent = await _queries.GetRecentActivityAsync(8, cancellationToken);

        // Always return all three buckets (in order) so the chart has a stable shape.
        var completeAging = Buckets
            .Select((bucket, index) => aging.FirstOrDefault(a => a.AgeBucket == bucket) ?? new AgingBucketDto(bucket, index + 1, 0, 0m))
            .ToList();

        return new DashboardDto(counts, completeAging, recent);
    }
}
