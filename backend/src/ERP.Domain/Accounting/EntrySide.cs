namespace ERP.Domain.Accounting;

/// <summary>Which side of the double entry a posting falls on.</summary>
/// <remarks>
/// Amounts throughout the accounting module are held as a positive figure plus a
/// side, never as a signed number. A negative debit and a positive credit describe
/// the same movement, and permitting both spellings means every report, every
/// balance, and every reconciliation has to normalise before it can sum - and one
/// that forgets produces a total that is quietly wrong rather than obviously so.
/// </remarks>
public enum EntrySide
{
    /// <summary>
    /// A debit. Increases assets and expenses; decreases liabilities, equity, and
    /// income.
    /// </summary>
    Debit = 1,

    /// <summary>
    /// A credit. Increases liabilities, equity, and income; decreases assets and
    /// expenses.
    /// </summary>
    Credit = 2,
}

/// <summary>Convenience operations on <see cref="EntrySide"/>.</summary>
public static class EntrySideExtensions
{
    /// <summary>Returns the opposite side.</summary>
    /// <param name="side">The side to invert.</param>
    /// <returns>The contra side.</returns>
    public static EntrySide Opposite(this EntrySide side) =>
        side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit;

    /// <summary>
    /// Returns the sign this side contributes to a balance held in debit-positive
    /// terms.
    /// </summary>
    /// <param name="side">The side.</param>
    /// <returns><c>+1</c> for a debit, <c>-1</c> for a credit.</returns>
    /// <remarks>
    /// Debit-positive is the convention used for the internal running balance:
    /// a trial balance sums to zero when it is correct, which makes the check
    /// trivial and the failure obvious.
    /// </remarks>
    public static int Sign(this EntrySide side) => side == EntrySide.Debit ? 1 : -1;
}
