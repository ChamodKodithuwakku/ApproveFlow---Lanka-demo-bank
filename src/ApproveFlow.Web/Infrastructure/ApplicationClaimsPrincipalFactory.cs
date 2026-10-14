using System.Globalization;
using System.Security.Claims;
using ApproveFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApproveFlow.Web.Infrastructure;

/// <summary>Adds display name and branch to the sign-in cookie so the UI does not query them on every request.</summary>
internal sealed class ApplicationClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public ApplicationClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaimTypes.FullName, user.FullName));
        identity.AddClaim(new Claim(AppClaimTypes.BranchId, user.BranchId.ToString(CultureInfo.InvariantCulture)));
        return identity;
    }
}
