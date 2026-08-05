using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Accounting;

/// <summary>
/// What a ledger represents, which decides where it appears and what extra
/// behaviour applies to it.
/// </summary>
/// <remarks>
/// The specification's sub-ledger list. A customer is a ledger like any other for
/// posting purposes, but only customer ledgers appear in an outstanding report,
/// only cash and bank ledgers appear in a cash book, and only cash and bank can be
/// the counter-account of a receipt or payment. Recording the kind is what lets
/// those screens filter without hard-coding ledger names.
/// </remarks>
public enum LedgerKind
{
    /// <summary>An ordinary ledger with no special behaviour.</summary>
    General = 1,

    /// <summary>Physical cash. Eligible as the counter-account of a cash voucher.</summary>
    Cash = 2,

    /// <summary>A bank account. Eligible as the counter-account of a bank voucher.</summary>
    Bank = 3,

    /// <summary>A customer, tracked for receivables and aging.</summary>
    Customer = 4,

    /// <summary>A supplier, tracked for payables and aging.</summary>
    Supplier = 5,

    /// <summary>An employee, for advances and payroll.</summary>
    Employee = 6,

    /// <summary>A tax head - VAT, CGST, SGST, IGST, cess.</summary>
    Tax = 7,

    /// <summary>
    /// An additional charge such as freight, packing, or a discount allowed.
    /// </summary>
    AdditionalCharge = 8,
}

/// <summary>
/// An account that transactions post against - the leaf of the chart of accounts.
/// </summary>
/// <remarks>
/// <para>
/// A ledger is a separate aggregate from <see cref="Voucher"/>, referenced by
/// identifier. That boundary is deliberate: posting a voucher must not be able to
/// mutate a ledger as a side effect. A ledger's balance is therefore never stored
/// as a field to be incremented - it is derived by summing postings, so it cannot
/// drift out of step with the entries that produce it.
/// </para>
/// <para>
/// The opening balance is the one exception, and it is an input rather than a
/// derived figure: it represents everything that happened before the system was
/// in use.
/// </para>
/// </remarks>
public sealed class Ledger : AggregateRoot<LedgerId>, IFirmScoped, IAuditable, ISoftDeletable
{
    private Ledger(
        LedgerId id,
        TenantId tenantId,
        FirmId firmId,
        AccountGroupId accountGroupId,
        string code,
        string name,
        LedgerKind kind,
        CurrencyCode currency)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        AccountGroupId = accountGroupId;
        Code = code;
        Name = name;
        Kind = kind;
        Currency = currency;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Ledger()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the group this ledger reports under.</summary>
    public AccountGroupId AccountGroupId { get; private set; }

    /// <summary>Gets the ledger code, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the ledger name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic, for RTL presentation.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets what this ledger represents.</summary>
    public LedgerKind Kind { get; private set; }

    /// <summary>
    /// Gets the currency this ledger is denominated in.
    /// </summary>
    /// <remarks>
    /// Usually the firm's base currency. A foreign-currency bank account is the
    /// common exception, and it is what makes exchange gain and loss arise.
    /// </remarks>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the opening balance, as at the start of the first financial year.</summary>
    public decimal OpeningBalance { get; private set; }

    /// <summary>Gets which side the opening balance falls on.</summary>
    public EntrySide OpeningBalanceSide { get; private set; } = EntrySide.Debit;

    /// <summary>Gets a value indicating whether the ledger accepts new postings.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets a value indicating whether bills against this ledger are settled
    /// individually.
    /// </summary>
    /// <remarks>
    /// Drives the specification's bill-wise settlement. When set, a receipt or
    /// payment against this ledger must allocate to specific outstanding
    /// documents rather than simply moving the balance.
    /// </remarks>
    public bool IsBillWise { get; private set; }

    /// <summary>Gets the credit limit, if one applies.</summary>
    public decimal? CreditLimit { get; private set; }

    /// <summary>Gets the agreed credit period in days, if one applies.</summary>
    public int? CreditDays { get; private set; }

    /// <summary>Gets the tax registration number, for a customer or supplier.</summary>
    public string? TaxRegistrationNumber { get; private set; }

    /// <summary>
    /// Gets the counterparty's state code, used to decide inter-state supply
    /// under Indian GST.
    /// </summary>
    public string? StateCode { get; private set; }

    /// <summary>Gets the contact telephone number.</summary>
    public string? Phone { get; private set; }

    /// <summary>Gets the contact mobile number, used for customer lookup at the till.</summary>
    public string? MobileNumber { get; private set; }

    /// <summary>Gets the contact email address.</summary>
    public string? Email { get; private set; }

    /// <summary>Gets the first address line.</summary>
    public string? AddressLine1 { get; private set; }

    /// <summary>Gets the second address line.</summary>
    public string? AddressLine2 { get; private set; }

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

    /// <summary>Gets a value indicating whether this ledger is cash or a bank account.</summary>
    /// <remarks>
    /// A cash or bank receipt must have one of these on the opposite side, which is
    /// what distinguishes it from a journal.
    /// </remarks>
    public bool IsCashOrBank => Kind is LedgerKind.Cash or LedgerKind.Bank;

    /// <summary>Gets a value indicating whether this ledger is a party account.</summary>
    public bool IsParty => Kind is LedgerKind.Customer or LedgerKind.Supplier or LedgerKind.Employee;

    /// <summary>Creates a ledger.</summary>
    /// <param name="group">The group it reports under.</param>
    /// <param name="code">The ledger code.</param>
    /// <param name="name">The ledger name.</param>
    /// <param name="kind">What the ledger represents.</param>
    /// <param name="currency">The denominating currency.</param>
    /// <returns>The ledger, or a validation failure.</returns>
    /// <remarks>
    /// Takes the group rather than its identifier so tenant and firm are inherited
    /// from it. A ledger in one firm reporting under another firm's group would
    /// corrupt both firms' statements, and passing an identifier would make that
    /// mistake possible.
    /// </remarks>
    public static Result<Ledger> Create(
        AccountGroup group,
        string code,
        string name,
        LedgerKind kind,
        CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Ledger>(Error.Validation(
                "Ledger.CodeRequired", "A ledger code is required."));
        }

        if (code.Trim().Length > 30)
        {
            return Result.Failure<Ledger>(Error.Validation(
                "Ledger.CodeTooLong", "A ledger code cannot exceed 30 characters."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Ledger>(Error.Validation(
                "Ledger.NameRequired", "A ledger name is required."));
        }

        if (!currency.IsSpecified)
        {
            return Result.Failure<Ledger>(Error.Validation(
                "Ledger.CurrencyRequired", "A ledger currency is required."));
        }

        if (!Enum.IsDefined(kind))
        {
            return Result.Failure<Ledger>(Error.Validation(
                "Ledger.UnknownKind", $"'{kind}' is not a recognised ledger kind."));
        }

        return Result.Success(new Ledger(
            LedgerId.NewId(), group.TenantId, group.FirmId, group.Id,
            code.Trim().ToUpperInvariant(), name.Trim(), kind, currency));
    }

    /// <summary>Sets the opening balance carried in from before the system was used.</summary>
    /// <param name="amount">The absolute amount. Must not be negative.</param>
    /// <param name="side">Which side the balance falls on.</param>
    /// <returns>Success, or a validation failure.</returns>
    /// <remarks>
    /// The amount is always positive and the side says which way it runs, rather
    /// than a signed figure. A negative debit and a positive credit are the same
    /// thing, and allowing both spellings means every report has to normalise
    /// before it can sum.
    /// </remarks>
    public Result SetOpeningBalance(decimal amount, EntrySide side)
    {
        if (amount < 0m)
        {
            return Result.Failure(Error.Validation(
                "Ledger.NegativeOpeningBalance",
                "An opening balance cannot be negative. Use the opposite side instead."));
        }

        OpeningBalance = amount;
        OpeningBalanceSide = side;

        return Result.Success();
    }

    /// <summary>Records the credit terms agreed with a party.</summary>
    /// <param name="creditLimit">The limit, or <see langword="null"/> for none.</param>
    /// <param name="creditDays">The period in days, or <see langword="null"/> for none.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetCreditTerms(decimal? creditLimit, int? creditDays)
    {
        if (creditLimit < 0m)
        {
            return Result.Failure(Error.Validation(
                "Ledger.NegativeCreditLimit", "A credit limit cannot be negative."));
        }

        if (creditDays < 0)
        {
            return Result.Failure(Error.Validation(
                "Ledger.NegativeCreditDays", "A credit period cannot be negative."));
        }

        CreditLimit = creditLimit;
        CreditDays = creditDays;

        return Result.Success();
    }

    /// <summary>Enables or disables bill-wise settlement for this ledger.</summary>
    /// <param name="isBillWise">Whether bills are settled individually.</param>
    public void SetBillWise(bool isBillWise) => IsBillWise = isBillWise;

    /// <summary>Records the party's tax registration and place of supply.</summary>
    /// <param name="registrationNumber">The VAT number or GSTIN.</param>
    /// <param name="stateCode">The party's state code.</param>
    public void SetTaxDetails(string? registrationNumber, string? stateCode)
    {
        TaxRegistrationNumber = Trimmed(registrationNumber);
        StateCode = Trimmed(stateCode)?.ToUpperInvariant();
    }

    /// <summary>Records the party's contact details.</summary>
    /// <param name="phone">The telephone number.</param>
    /// <param name="mobileNumber">The mobile number, used for lookup at the till.</param>
    /// <param name="email">The email address.</param>
    /// <param name="addressLine1">The first address line.</param>
    /// <param name="addressLine2">The second address line.</param>
    public void SetContactDetails(
        string? phone,
        string? mobileNumber,
        string? email,
        string? addressLine1,
        string? addressLine2)
    {
        Phone = Trimmed(phone);
        MobileNumber = Trimmed(mobileNumber);
        Email = Trimmed(email);
        AddressLine1 = Trimmed(addressLine1);
        AddressLine2 = Trimmed(addressLine2);
    }

    /// <summary>Sets the Arabic name shown in RTL mode.</summary>
    /// <param name="nameArabic">The Arabic name, or <see langword="null"/> to clear it.</param>
    public void SetArabicName(string? nameArabic) => NameArabic = Trimmed(nameArabic);

    /// <summary>Renames the ledger.</summary>
    /// <param name="name">The new name.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "Ledger.NameRequired", "A ledger name is required."));
        }

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Stops the ledger accepting new postings.</summary>
    /// <remarks>
    /// Historical entries are untouched, so past reports continue to reconcile.
    /// This is why a ledger is deactivated rather than deleted.
    /// </remarks>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows the ledger to accept postings again.</summary>
    public void Activate() => IsActive = true;

    /// <summary>Checks whether the ledger may be posted to.</summary>
    /// <returns>Success, or the reason it may not.</returns>
    public Result EnsurePostable() => IsActive
        ? Result.Success()
        : Result.Failure(Error.BusinessRule(
            "Ledger.Inactive",
            $"Ledger '{Name}' is inactive and cannot be posted to."));

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
