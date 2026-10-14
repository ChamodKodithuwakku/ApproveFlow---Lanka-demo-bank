using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _db;

    public UnitOfWork(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another user saved the same row first (RowVersion no longer matches).
            throw new ConcurrencyConflictException(ex);
        }
    }
}
