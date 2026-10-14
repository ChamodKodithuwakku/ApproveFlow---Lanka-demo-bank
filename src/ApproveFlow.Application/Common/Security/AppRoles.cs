namespace ApproveFlow.Application.Common.Security;

/// <summary>Application roles. Role names are stored in ASP.NET Core Identity and issued as claims.</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Maker = "Maker";
    public const string Checker = "Checker";
    public const string Auditor = "Auditor";

    public static readonly IReadOnlyList<string> All = new[] { Admin, Maker, Checker, Auditor };

    /// <summary>
    /// Segregation of duties for role assignment: a user may not be both Maker and Checker, and an Auditor
    /// holds no other role (independence of the audit function).
    /// </summary>
    public static string? ValidateCombination(IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Count == 0)
        {
            return "At least one role is required.";
        }

        if (roles.Any(r => !All.Contains(r, StringComparer.Ordinal)))
        {
            return "Unknown role.";
        }

        if (roles.Contains(Maker) && roles.Contains(Checker))
        {
            return "Segregation of duties: a user cannot be both Maker and Checker.";
        }

        if (roles.Contains(Auditor) && roles.Count > 1)
        {
            return "Segregation of duties: an Auditor cannot hold any other role.";
        }

        return null;
    }
}
