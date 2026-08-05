namespace ERP.SharedKernel.Abstractions;

/// <summary>Supplies the current time.</summary>
/// <remarks>
/// <para>
/// Domain code never calls <see cref="DateTimeOffset.UtcNow"/> directly. Time
/// is an input, and an untestable one when read from a static: a financial-year
/// boundary rule, a cheque maturing, an aging bucket, or a batch expiring cannot
/// be tested without being able to state what "now" is.
/// </para>
/// <para>
/// <see cref="TodayIn"/> exists because "today" is genuinely ambiguous in this
/// product. A branch in Doha and one in Kerala are on different dates for part
/// of every day, so a day-book or Z-report boundary must be evaluated in the
/// branch's own time zone, not the server's.
/// </para>
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Gets the calendar date currently in effect in the given time zone.
    /// </summary>
    /// <param name="timeZone">The branch or firm time zone.</param>
    /// <returns>The local calendar date.</returns>
    DateOnly TodayIn(TimeZoneInfo timeZone);
}
