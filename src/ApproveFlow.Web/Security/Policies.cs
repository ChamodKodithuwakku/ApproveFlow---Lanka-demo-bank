using ApproveFlow.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace ApproveFlow.Web.Security;

/// <summary>
/// Named authorization policies. Every controller/action declares one; the fallback policy additionally requires an
/// authenticated user for anything that forgets to (deny by default). Services repeat the role checks server-side.
/// </summary>
public static class Policies
{
    public const string AnyStaff = nameof(AnyStaff);
    public const string MakerOnly = nameof(MakerOnly);
    public const string CheckerOnly = nameof(CheckerOnly);
    public const string AdminOnly = nameof(AdminOnly);
    public const string AuditorOnly = nameof(AuditorOnly);
    public const string ReportViewer = nameof(ReportViewer);
    public const string RiskSummary = nameof(RiskSummary);
    public const string CustomerManager = nameof(CustomerManager);

    public static void Configure(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(AnyStaff, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.All));
        options.AddPolicy(MakerOnly, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Maker));
        options.AddPolicy(CheckerOnly, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Checker));
        options.AddPolicy(AdminOnly, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Admin));
        options.AddPolicy(AuditorOnly, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Auditor));
        options.AddPolicy(ReportViewer, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Admin, AppRoles.Checker, AppRoles.Auditor));
        options.AddPolicy(RiskSummary, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Checker, AppRoles.Auditor));
        options.AddPolicy(CustomerManager, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Maker, AppRoles.Admin));

        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }
}
