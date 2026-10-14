using ApproveFlow.Application.Abstractions.Identity;
using ApproveFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Identity;

internal sealed class UserDirectory : IUserDirectory
{
    private readonly ApplicationDbContext _db;

    public UserDirectory(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<UserInfo?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        // Read fresh from the database on every decision so a changed approval limit or deactivation applies at once.
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.FullName, u.BranchId, BranchName = u.Branch!.Name, u.ApprovalLimit, u.IsActive })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return null;
        }

        var roles = await (from ur in _db.UserRoles
                           join r in _db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == userId
                           select r.Name!).ToListAsync(cancellationToken);

        return new UserInfo(user.Id, user.Email ?? string.Empty, user.FullName, user.BranchId, user.BranchName, user.ApprovalLimit, user.IsActive, roles);
    }
}
