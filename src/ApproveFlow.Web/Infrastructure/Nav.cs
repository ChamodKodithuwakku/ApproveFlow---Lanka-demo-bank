using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApproveFlow.Web.Infrastructure;

/// <summary>Navigation helpers for the layout.</summary>
public static class Nav
{
    public static string Active(ViewContext context, string controller, string? action = null, string? area = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var values = context.RouteData.Values;
        var matches = string.Equals(values["controller"] as string, controller, StringComparison.OrdinalIgnoreCase)
                      && (action is null || string.Equals(values["action"] as string, action, StringComparison.OrdinalIgnoreCase))
                      && string.Equals(values["area"] as string ?? string.Empty, area ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return matches ? "active" : string.Empty;
    }
}

/// <summary>Model for the shared pager partial.</summary>
public sealed record PagerModel(int Page, int TotalPages, int TotalCount, Func<int, string?> UrlFor);
