using ERP.SharedKernel.Abstractions;

namespace ERP.Infrastructure.Time;

/// <summary>The real clock, reading the operating system time.</summary>
/// <remarks>
/// The only place in the application permitted to read the ambient time. Domain
/// and application code take <see cref="IClock"/> so that financial-year
/// boundaries, cheque maturity, aging buckets, and batch expiry can all be tested
/// at a chosen instant rather than whenever the suite happens to run.
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateOnly TodayIn(TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
    }
}
