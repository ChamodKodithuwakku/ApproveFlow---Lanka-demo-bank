namespace ApproveFlow.Application.Common.Time;

/// <summary>
/// The bank operates in Asia/Colombo (UTC+05:30, no daylight saving). All timestamps are stored in UTC and
/// converted only for display and for "business day" boundaries.
/// </summary>
public static class ColomboTime
{
    public static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime? FromUtc(DateTime? utc) => utc.HasValue ? FromUtc(utc.Value) : null;

    public static DateOnly Today(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(FromUtc(clock.GetUtcNow().UtcDateTime));
    }

    /// <summary>UTC instant at which the given Colombo calendar day starts.</summary>
    public static DateTime StartOfDayUtc(DateOnly localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);

    /// <summary>Half-open UTC range [from, to+1day) for inclusive local-date filters.</summary>
    public static (DateTime? FromUtc, DateTime? ToUtcExclusive) ToUtcRange(DateOnly? from, DateOnly? to) =>
        (from.HasValue ? StartOfDayUtc(from.Value) : null, to.HasValue ? StartOfDayUtc(to.Value.AddDays(1)) : null);

    public static string Format(DateTime utc) =>
        FromUtc(utc).ToString("dd MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public static string Format(DateTime? utc) => utc.HasValue ? Format(utc.Value) : "—";

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Asia/Colombo", "Sri Lanka Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        // Sri Lanka has used a fixed +05:30 offset since 2006.
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Colombo", TimeSpan.FromMinutes(330), "Asia/Colombo", "Sri Lanka Standard Time");
    }
}
