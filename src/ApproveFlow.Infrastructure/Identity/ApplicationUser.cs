using ApproveFlow.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace ApproveFlow.Infrastructure.Identity;

/// <summary>Bank staff user. Extends ASP.NET Core Identity with branch, approval limit and an active flag.</summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>Maximum amount (LKR) a Checker can approve alone. Null for non-checkers.</summary>
    public decimal? ApprovalLimit { get; set; }

    /// <summary>Deactivated users cannot sign in or use previously issued API tokens.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
