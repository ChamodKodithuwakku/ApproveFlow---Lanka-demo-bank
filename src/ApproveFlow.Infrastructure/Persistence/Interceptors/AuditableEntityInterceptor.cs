using System.Security.Cryptography;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApproveFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Fills created/updated stamps from the current user and, on providers without a native rowversion type,
/// renews the optimistic-concurrency token on every insert/update.
/// </summary>
public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
    internal const string SystemUserId = "system";
    internal const string SystemUserName = "system";

    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AuditableEntityInterceptor(ICurrentUser currentUser, TimeProvider clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var userId = _currentUser.IsAuthenticated && _currentUser.UserId is not null ? _currentUser.UserId : SystemUserId;
        var userName = _currentUser.IsAuthenticated ? _currentUser.FullName ?? _currentUser.UserName ?? userId : SystemUserName;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.StampCreated(userId, userName, now);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.StampUpdated(userId, userName, now);
            }
        }

        var nativeRowVersion = context is ApplicationDbContext { IsSqlServer: true };
        if (!nativeRowVersion)
        {
            foreach (var entry in context.ChangeTracker.Entries<LoanApplication>())
            {
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    entry.Property(a => a.RowVersion).CurrentValue = RandomNumberGenerator.GetBytes(8);
                }
            }
        }
    }
}
