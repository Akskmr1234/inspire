using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Accounting;

/// <summary>The kinds of accounting voucher the specification requires.</summary>
public enum VoucherType
{
    /// <summary>Money received into cash.</summary>
    CashReceipt = 1,

    /// <summary>Money received into a bank account.</summary>
    BankReceipt = 2,

    /// <summary>Money paid out of cash.</summary>
    CashPayment = 3,

    /// <summary>Money paid out of a bank account.</summary>
    BankPayment = 4,

    /// <summary>A general journal entry, with no cash or bank movement.</summary>
    Journal = 5,

    /// <summary>
    /// A transfer between the firm's own cash and bank accounts.
    /// </summary>
    /// <remarks>
    /// Distinct from a payment because no third party is involved: both sides are
    /// the firm's own money, so it must never appear in a payables or receivables
    /// report.
    /// </remarks>
    Contra = 6,

    /// <summary>The entry establishing balances brought forward into the system.</summary>
    OpeningBalance = 7,
}

/// <summary>Where a voucher stands in its lifecycle.</summary>
public enum VoucherStatus
{
    /// <summary>Being entered. Editable, and excluded from every balance and report.</summary>
    Draft = 1,

    /// <summary>
    /// Posted to the ledgers. Included in balances, and no longer editable.
    /// </summary>
    Posted = 2,

    /// <summary>
    /// Reversed out. Retained in full, and excluded from balances.
    /// </summary>
    /// <remarks>
    /// Cancellation rather than deletion, because a voucher number that simply
    /// vanished would leave an unexplained gap in the sequence - which is precisely
    /// what an auditor looks for.
    /// </remarks>
    Cancelled = 3,
}

/// <summary>
/// A double-entry accounting document: a set of ledger postings that balance.
/// </summary>
/// <remarks>
/// <para>
/// The voucher and its lines form a single aggregate, because the rule that gives
/// double-entry bookkeeping its meaning - total debits equal total credits - spans
/// all of them and can only be guaranteed if they are saved together. Splitting
/// lines into their own aggregate would allow a half-saved voucher, and a half-saved
/// voucher is an unbalanced ledger.
/// </para>
/// <para>
/// Ledgers are referenced by identifier and never loaded into this aggregate. A
/// ledger's balance is derived by summing postings rather than stored and
/// incremented, so posting a voucher cannot leave a balance out of step with the
/// entries behind it.
/// </para>
/// </remarks>
public sealed class Voucher : AggregateRoot<VoucherId>, IBranchScoped, IAuditable, ISoftDeletable
{
    private readonly List<VoucherLine> _lines = [];

    private Voucher(
        VoucherId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        VoucherType type,
        string number,
        DateOnly date,
        CurrencyCode currency,
        CurrencyCode baseCurrency,
        decimal exchangeRate)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        BranchId = branchId;
        FinancialYearId = financialYearId;
        Type = type;
        Number = number;
        Date = date;
        Currency = currency;
        BaseCurrency = baseCurrency;
        ExchangeRate = exchangeRate;
        Status = VoucherStatus.Draft;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Voucher()
    {
        Number = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <inheritdoc />
    public BranchId BranchId { get; private set; }

    /// <summary>Gets the financial year this voucher posts into.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets the kind of voucher.</summary>
    public VoucherType Type { get; private set; }

    /// <summary>
    /// Gets the document number, produced by the branch's numbering series.
    /// </summary>
    /// <remarks>
    /// Assigned at creation and never changed. Renumbering a posted voucher would
    /// break every reference to it, printed or otherwise.
    /// </remarks>
    public string Number { get; private set; }

    /// <summary>Gets the document date, which decides the period it falls in.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the currency the amounts were entered in.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the firm's base currency, which the books are kept in.</summary>
    public CurrencyCode BaseCurrency { get; private set; }

    /// <summary>
    /// Gets the rate converting one unit of <see cref="Currency"/> into
    /// <see cref="BaseCurrency"/>.
    /// </summary>
    /// <remarks>
    /// Stored on the voucher rather than looked up at read time. The rate that
    /// applied on the transaction date is a fact about the transaction, and a
    /// report re-run next year must reproduce the same base-currency figures it
    /// showed when the voucher was posted.
    /// </remarks>
    public decimal ExchangeRate { get; private set; } = 1m;

    /// <summary>Gets the reference or invoice number the voucher relates to.</summary>
    public string? ReferenceNumber { get; private set; }

    /// <summary>Gets the voucher-level narration.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets the payment mode, for a receipt or payment.</summary>
    public string? PaymentMode { get; private set; }

    /// <summary>Gets the current lifecycle state.</summary>
    public VoucherStatus Status { get; private set; }

    /// <summary>Gets the instant the voucher was posted, in UTC.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Gets the user who posted the voucher.</summary>
    public UserId? PostedBy { get; private set; }

    /// <summary>Gets the reason the voucher was cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Gets the voucher's lines.</summary>
    public IReadOnlyList<VoucherLine> Lines => _lines.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? DeletedBy { get; private set; }

    /// <summary>Gets a value indicating whether the voucher is still editable.</summary>
    public bool IsEditable => Status == VoucherStatus.Draft;

    /// <summary>Gets the total of the debit lines, in the entry currency.</summary>
    public Money TotalDebit => SumSide(EntrySide.Debit);

    /// <summary>Gets the total of the credit lines, in the entry currency.</summary>
    public Money TotalCredit => SumSide(EntrySide.Credit);

    /// <summary>
    /// Gets the amount by which the voucher fails to balance, in the entry currency.
    /// </summary>
    /// <remarks>
    /// Surfaced so the entry screen can show a running difference as the user
    /// types, which is how an accountant finds the transposed digit. Zero on a
    /// balanced voucher.
    /// </remarks>
    public Money Difference => TotalDebit - TotalCredit;

    /// <summary>Gets a value indicating whether debits equal credits.</summary>
    public bool IsBalanced => Difference.IsZero;

    /// <summary>Creates a draft voucher.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="branchId">The originating branch.</param>
    /// <param name="financialYear">The year to post into.</param>
    /// <param name="type">The kind of voucher.</param>
    /// <param name="number">The document number from the numbering series.</param>
    /// <param name="date">The document date.</param>
    /// <param name="currency">The currency amounts are entered in.</param>
    /// <param name="baseCurrency">The firm's base currency.</param>
    /// <param name="exchangeRate">
    /// The rate from <paramref name="currency"/> to
    /// <paramref name="baseCurrency"/>. Must be 1 when they are the same.
    /// </param>
    /// <returns>The draft voucher, or a validation failure.</returns>
    public static Result<Voucher> CreateDraft(
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYear financialYear,
        VoucherType type,
        string number,
        DateOnly date,
        CurrencyCode currency,
        CurrencyCode baseCurrency,
        decimal exchangeRate = 1m)
    {
        ArgumentNullException.ThrowIfNull(financialYear);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<Voucher>(Error.Validation(
                "Voucher.NumberRequired", "A voucher number is required."));
        }

        if (!Enum.IsDefined(type))
        {
            return Result.Failure<Voucher>(Error.Validation(
                "Voucher.UnknownType", $"'{type}' is not a recognised voucher type."));
        }

        if (!currency.IsSpecified || !baseCurrency.IsSpecified)
        {
            return Result.Failure<Voucher>(Error.Validation(
                "Voucher.CurrencyRequired", "Both an entry currency and a base currency are required."));
        }

        if (exchangeRate <= 0m)
        {
            return Result.Failure<Voucher>(Error.Validation(
                "Voucher.ExchangeRateInvalid",
                $"An exchange rate must be greater than zero, but {exchangeRate} was supplied."));
        }

        // A rate other than 1 between identical currencies would silently restate
        // the books - every base figure inflated or deflated with nothing to
        // indicate why.
        if (currency == baseCurrency && exchangeRate != 1m)
        {
            return Result.Failure<Voucher>(Error.Validation(
                "Voucher.ExchangeRateMustBeOne",
                $"The entry currency and base currency are both {currency}, so the " +
                $"exchange rate must be 1, not {exchangeRate}."));
        }

        // The single gate covering both the date range and whether the year is
        // still open.
        Result canPost = financialYear.CanPostOn(date);

        if (canPost.IsFailure)
        {
            return Result.Failure<Voucher>(canPost.Error);
        }

        return Result.Success(new Voucher(
            VoucherId.NewId(), tenantId, firmId, branchId, financialYear.Id,
            type, number.Trim(), date, currency, baseCurrency, exchangeRate));
    }

    /// <summary>Adds a line to a draft voucher.</summary>
    /// <param name="ledgerId">The ledger to post against.</param>
    /// <param name="side">Whether the line debits or credits that ledger.</param>
    /// <param name="amount">The amount, in the entry currency. Must be positive.</param>
    /// <param name="narration">A line-level narration.</param>
    /// <returns>The line, or a failure.</returns>
    public Result<VoucherLine> AddLine(
        LedgerId ledgerId,
        EntrySide side,
        decimal amount,
        string? narration = null)
    {
        if (!IsEditable)
        {
            return Result.Failure<VoucherLine>(Error.BusinessRule(
                "Voucher.NotEditable",
                $"Voucher '{Number}' is {Status} and can no longer be changed."));
        }

        // Zero is rejected as well as negative. A zero line contributes nothing,
        // survives the balance check, and shows up on a printed voucher as a
        // baffling blank row.
        if (amount <= 0m)
        {
            return Result.Failure<VoucherLine>(Error.Validation(
                "Voucher.LineAmountNotPositive",
                $"A line amount must be greater than zero, but {amount} was supplied. " +
                $"Use the opposite side rather than a negative amount."));
        }

        if (!Enum.IsDefined(side))
        {
            return Result.Failure<VoucherLine>(Error.Validation(
                "Voucher.UnknownEntrySide", $"'{side}' is not a recognised entry side."));
        }

        VoucherLine line = new(
            VoucherLineId.NewId(),
            TenantId,
            Id,
            ledgerId,
            side,
            Money.Of(amount, Currency),
            _lines.Count + 1,
            narration?.Trim());

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Removes a line from a draft voucher.</summary>
    /// <param name="lineId">The line to remove.</param>
    /// <returns>Success, or a failure.</returns>
    public Result RemoveLine(VoucherLineId lineId)
    {
        if (!IsEditable)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.NotEditable",
                $"Voucher '{Number}' is {Status} and can no longer be changed."));
        }

        int removed = _lines.RemoveAll(l => l.Id == lineId);

        if (removed == 0)
        {
            return Result.Failure(Error.NotFound(
                "Voucher.LineNotFound", "That line does not belong to this voucher."));
        }

        Renumber();

        return Result.Success();
    }

    /// <summary>Sets the voucher-level descriptive fields.</summary>
    /// <param name="referenceNumber">The related reference or invoice number.</param>
    /// <param name="narration">The narration.</param>
    /// <param name="paymentMode">The payment mode.</param>
    /// <returns>Success, or a failure.</returns>
    public Result SetDetails(string? referenceNumber, string? narration, string? paymentMode)
    {
        if (!IsEditable)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.NotEditable",
                $"Voucher '{Number}' is {Status} and can no longer be changed."));
        }

        ReferenceNumber = Trimmed(referenceNumber);
        Narration = Trimmed(narration);
        PaymentMode = Trimmed(paymentMode);

        return Result.Success();
    }

    /// <summary>
    /// Posts the voucher to the ledgers, after checking every invariant that makes
    /// it a valid double entry.
    /// </summary>
    /// <param name="postedBy">The user posting it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success, or the first invariant that fails.</returns>
    /// <remarks>
    /// The one gate between a draft and the firm's books. Nothing enters a balance
    /// without passing through here, which is why the checks live in the domain
    /// rather than in a validator that a second caller could bypass.
    /// </remarks>
    public Result Post(UserId postedBy, DateTimeOffset nowUtc)
    {
        if (Status != VoucherStatus.Draft)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.AlreadyPosted",
                $"Voucher '{Number}' is already {Status}."));
        }

        // A single line cannot balance against anything: double entry needs both a
        // source and a destination.
        if (_lines.Count < 2)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.TooFewLines",
                $"A voucher needs at least two lines to balance, but '{Number}' has " +
                $"{_lines.Count}."));
        }

        if (!_lines.Exists(l => l.Side == EntrySide.Debit)
            || !_lines.Exists(l => l.Side == EntrySide.Credit))
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.SingleSided",
                $"Voucher '{Number}' has lines on only one side. Every debit needs a " +
                $"corresponding credit."));
        }

        if (TotalDebit.IsZero)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.ZeroValue",
                $"Voucher '{Number}' totals zero and would record nothing."));
        }

        if (!IsBalanced)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.NotBalanced",
                $"Voucher '{Number}' does not balance: debits total " +
                $"{TotalDebit.Amount} and credits total {TotalCredit.Amount}, a " +
                $"difference of {Difference.Amount} {Currency}."));
        }

        AssignBaseAmounts();

        Status = VoucherStatus.Posted;
        PostedAtUtc = nowUtc;
        PostedBy = postedBy;

        Raise(new VoucherPosted(
            Id, TenantId, FirmId, BranchId, Type, Number, Date, TotalDebit));

        return Result.Success();
    }

    /// <summary>Cancels a posted voucher, reversing its effect on the ledgers.</summary>
    /// <param name="reason">Why it is being cancelled. Required.</param>
    /// <returns>Success, or a failure.</returns>
    /// <remarks>
    /// The voucher and its number are retained. A number that vanished would leave
    /// a gap in the sequence with no explanation, which is exactly what an audit
    /// treats as suspicious.
    /// </remarks>
    public Result Cancel(string reason)
    {
        if (Status != VoucherStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.NotPosted",
                $"Only a posted voucher can be cancelled, and '{Number}' is {Status}."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "Voucher.CancellationReasonRequired",
                "A reason is required when cancelling a voucher."));
        }

        Status = VoucherStatus.Cancelled;
        CancellationReason = reason.Trim();

        Raise(new VoucherCancelled(Id, TenantId, Number, CancellationReason));

        return Result.Success();
    }

    /// <summary>
    /// Converts every line into the base currency, preserving the balance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Converting each line independently would break the books. At a rate of
    /// 3.6405, two credit lines of 100.00 and 50.00 round to 364.05 and 182.03
    /// (546.08), while the single debit line of 150.00 rounds to 546.08 - or, with
    /// different figures, does not. Any residual difference means base-currency
    /// debits and credits disagree, and the trial balance fails to balance for a
    /// reason nobody can see on the voucher.
    /// </para>
    /// <para>
    /// Instead the <em>total</em> is converted once, and that converted total is
    /// allocated across each side's lines in proportion to their amounts. Because
    /// the two sides have equal totals in the entry currency, they receive the
    /// identical converted total, and allocation distributes it without losing a
    /// minor unit. Base debits therefore equal base credits exactly, by
    /// construction rather than by luck.
    /// </para>
    /// </remarks>
    private void AssignBaseAmounts()
    {
        if (Currency == BaseCurrency)
        {
            foreach (VoucherLine line in _lines)
            {
                line.AssignBaseAmount(Money.Of(line.Amount.Amount, BaseCurrency));
            }

            return;
        }

        Money convertedTotal = Money.Of(TotalDebit.Amount * ExchangeRate, BaseCurrency);

        AllocateSide(EntrySide.Debit, convertedTotal);
        AllocateSide(EntrySide.Credit, convertedTotal);
    }

    private void AllocateSide(EntrySide side, Money convertedTotal)
    {
        List<VoucherLine> lines = _lines.FindAll(l => l.Side == side);

        if (lines.Count == 0)
        {
            return;
        }

        // Ratios are the line amounts expressed in whole minor units of the entry
        // currency, so allocation works in integers and cannot lose a fraction.
        decimal scale = Pow10(Currency.DecimalPlaces);

        long[] ratios = new long[lines.Count];

        for (int i = 0; i < lines.Count; i++)
        {
            ratios[i] = (long)decimal.Round(
                lines[i].Amount.Amount * scale, 0, MidpointRounding.AwayFromZero);
        }

        Money[] shares = convertedTotal.Allocate(ratios);

        for (int i = 0; i < lines.Count; i++)
        {
            lines[i].AssignBaseAmount(shares[i]);
        }
    }

    private static decimal Pow10(int exponent) => exponent switch
    {
        0 => 1m,
        1 => 10m,
        2 => 100m,
        3 => 1_000m,
        4 => 10_000m,
        _ => (decimal)Math.Pow(10, exponent),
    };

    private Money SumSide(EntrySide side)
    {
        Money total = Money.Zero(Currency);

        foreach (VoucherLine line in _lines)
        {
            if (line.Side == side)
            {
                total += line.Amount;
            }
        }

        return total;
    }

    private void Renumber()
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            _lines[i].SetLineNumber(i + 1);
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
