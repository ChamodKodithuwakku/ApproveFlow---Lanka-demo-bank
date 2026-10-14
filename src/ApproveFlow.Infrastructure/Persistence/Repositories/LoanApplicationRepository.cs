using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.Common.Time;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Persistence.Repositories;

internal sealed class LoanApplicationRepository : ILoanApplicationRepository
{
    private readonly ApplicationDbContext _db;

    public LoanApplicationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<LoanApplication?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default) =>
        _db.LoanApplications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<LoanApplication?> GetDetailsAsync(int id, CancellationToken cancellationToken = default) =>
        _db.LoanApplications
            .AsNoTracking()
            .Include(a => a.Customer)
            .Include(a => a.LoanProduct)
            .Include(a => a.Branch)
            .Include(a => a.Actions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApprovalAction>> GetHistoryAsync(int id, CancellationToken cancellationToken = default) =>
        await _db.ApprovalActions
            .AsNoTracking()
            .Where(a => a.LoanApplicationId == id)
            .OrderBy(a => a.PerformedAtUtc).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<LoanApplicationListItemDto>> SearchAsync(LoanApplicationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);

        var source = _db.LoanApplications.AsNoTracking();

        if (query.Status.HasValue)
        {
            source = source.Where(a => a.Status == query.Status.Value);
        }
        else if (query.Statuses is { Count: > 0 })
        {
            var statuses = query.Statuses.ToList();
            source = source.Where(a => statuses.Contains(a.Status));
        }

        if (query.BranchId.HasValue)
        {
            source = source.Where(a => a.BranchId == query.BranchId.Value);
        }

        if (query.ProductId.HasValue)
        {
            source = source.Where(a => a.LoanProductId == query.ProductId.Value);
        }

        if (!string.IsNullOrEmpty(query.CreatedById))
        {
            source = source.Where(a => a.CreatedById == query.CreatedById);
        }

        var (fromUtc, toUtcExclusive) = ColomboTime.ToUtcRange(query.FromDate, query.ToDate);
        if (fromUtc.HasValue)
        {
            source = source.Where(a => a.CreatedAtUtc >= fromUtc.Value);
        }

        if (toUtcExclusive.HasValue)
        {
            source = source.Where(a => a.CreatedAtUtc < toUtcExclusive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Parameterised LIKE via LINQ; the search term is data, never SQL.
            var term = query.Search.Trim();
            source = source.Where(a =>
                a.ReferenceNumber.Contains(term) ||
                a.Customer!.FullName.Contains(term) ||
                a.Customer!.Nic.Contains(term));
        }

        var descending = query.SortDirection == SortDirection.Descending;
        IOrderedQueryable<LoanApplication> ordered = (query.SortBy?.ToLowerInvariant()) switch
        {
            LoanApplicationSortFields.Reference => descending ? source.OrderByDescending(a => a.ReferenceNumber) : source.OrderBy(a => a.ReferenceNumber),
            LoanApplicationSortFields.Customer => descending ? source.OrderByDescending(a => a.Customer!.FullName) : source.OrderBy(a => a.Customer!.FullName),
            LoanApplicationSortFields.Amount => descending ? source.OrderByDescending(a => a.Amount) : source.OrderBy(a => a.Amount),
            LoanApplicationSortFields.Status => descending ? source.OrderByDescending(a => a.Status) : source.OrderBy(a => a.Status),
            LoanApplicationSortFields.Submitted => descending ? source.OrderByDescending(a => a.SubmittedAtUtc) : source.OrderBy(a => a.SubmittedAtUtc),
            _ => descending ? source.OrderByDescending(a => a.CreatedAtUtc) : source.OrderBy(a => a.CreatedAtUtc),
        };

        var total = await source.CountAsync(cancellationToken);
        var items = await ordered.ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new LoanApplicationListItemDto(
                a.Id,
                a.ReferenceNumber,
                a.Customer!.FullName,
                a.Branch!.Name,
                a.LoanProduct!.Name,
                a.Amount,
                a.TenureMonths,
                a.DebtBurdenRatio,
                a.Status,
                a.CreatedById,
                a.CreatedByName,
                a.CreatedAtUtc,
                a.SubmittedAtUtc,
                a.FirstApproverId,
                a.IsImported))
            .ToListAsync(cancellationToken);

        return new PagedResult<LoanApplicationListItemDto>(items, page, pageSize, total);
    }

    public void Add(LoanApplication application) => _db.LoanApplications.Add(application);

    public void SetExpectedRowVersion(LoanApplication application, byte[] rowVersion)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        _db.Entry(application).Property(a => a.RowVersion).OriginalValue = rowVersion;
    }
}
