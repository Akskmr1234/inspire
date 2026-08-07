using System.Globalization;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Numbering;

/// <summary>Identifies a numbering series.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct NumberingSeriesId(Guid Value) : IStronglyTypedId<NumberingSeriesId>
{
    /// <inheritdoc />
    public static NumberingSeriesId From(Guid value) => new(value);

    /// <inheritdoc />
    public static NumberingSeriesId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Produces the document numbers for one document type, in one branch, for one
/// financial year.
/// </summary>
/// <remarks>
/// <para>
/// Implements section 11 of the specification: a configurable prefix, suffix,
/// starting number, number length, and optional financial-year and branch
/// segments. All four worked examples fall out of the same format:
/// </para>
/// <list type="bullet">
/// <item><description><c>SL001</c> - prefix only, no separator.</description></item>
/// <item><description><c>001-SL</c> - suffix only, hyphen separator.</description></item>
/// <item><description><c>SL001A</c> - prefix and suffix, no separator.</description></item>
/// <item><description><c>SL/2026/0001</c> - prefix, financial year, slash separator.</description></item>
/// </list>
/// <para>
/// <see cref="DocumentType"/> is a string rather than an enum on purpose. The
/// specification requires numbering to be configurable without a deployment, and
/// new document types arrive with every module; an enum would mean a code change
/// and a migration each time. <see cref="DocumentTypes"/> names the ones the
/// shipped screens use.
/// </para>
/// </remarks>
public sealed class NumberingSeries : AggregateRoot<NumberingSeriesId>, IFirmScoped, IAuditable
{
    /// <summary>The highest number this series can reach before the length overflows.</summary>
    private const int MaximumNumberLength = 12;

    private NumberingSeries(
        NumberingSeriesId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId? branchId,
        FinancialYearId? financialYearId,
        string documentType,
        int startingNumber,
        int numberLength)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        BranchId = branchId;
        FinancialYearId = financialYearId;
        DocumentType = documentType;
        StartingNumber = startingNumber;
        NumberLength = numberLength;
        NextNumber = startingNumber;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private NumberingSeries()
    {
        DocumentType = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>
    /// Gets the branch this series is confined to, or <see langword="null"/> when
    /// numbering is shared across the whole firm.
    /// </summary>
    /// <remarks>
    /// The specification's branch-wise toggle. Null means one shared sequence;
    /// a value means each branch counts independently, so two branches can both
    /// hold invoice 0001 without colliding.
    /// </remarks>
    public BranchId? BranchId { get; private set; }

    /// <summary>
    /// Gets the financial year this series is confined to, or <see langword="null"/>
    /// when the sequence runs continuously across years.
    /// </summary>
    /// <remarks>
    /// The specification's financial-year-wise toggle. Most jurisdictions expect
    /// the sequence to restart each year, which is why the year also appears in the
    /// formatted number.
    /// </remarks>
    public FinancialYearId? FinancialYearId { get; private set; }

    /// <summary>Gets the document type this series numbers, for example <c>sales.invoice</c>.</summary>
    public string DocumentType { get; private set; }

    /// <summary>Gets the text placed before the number.</summary>
    public string? Prefix { get; private set; }

    /// <summary>Gets the text placed after the number.</summary>
    public string? Suffix { get; private set; }

    /// <summary>Gets the string joining the segments, such as <c>/</c> or <c>-</c>.</summary>
    public string Separator { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the financial-year label inserted into the number, such as <c>2026</c>.
    /// </summary>
    /// <remarks>
    /// Held on the series rather than read from the financial year at format time,
    /// so a formatted number never changes because somebody renamed the year.
    /// </remarks>
    public string? FinancialYearLabel { get; private set; }

    /// <summary>Gets the first number the series issues.</summary>
    public int StartingNumber { get; private set; }

    /// <summary>Gets the number of digits the counter is padded to.</summary>
    public int NumberLength { get; private set; }

    /// <summary>Gets the number that will be issued next.</summary>
    public int NextNumber { get; private set; }

    /// <summary>Gets a value indicating whether the series still issues numbers.</summary>
    public bool IsActive { get; private set; } = true;

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Creates a numbering series.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="documentType">The document type to number.</param>
    /// <param name="branchId">The branch, or <see langword="null"/> to share firm-wide.</param>
    /// <param name="financialYearId">The year, or <see langword="null"/> to run continuously.</param>
    /// <param name="startingNumber">The first number to issue.</param>
    /// <param name="numberLength">The digits to pad to.</param>
    /// <returns>The series, or a validation failure.</returns>
    public static Result<NumberingSeries> Create(
        TenantId tenantId,
        FirmId firmId,
        string documentType,
        BranchId? branchId = null,
        FinancialYearId? financialYearId = null,
        int startingNumber = 1,
        int numberLength = 4)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return Result.Failure<NumberingSeries>(Error.Validation(
                "NumberingSeries.DocumentTypeRequired", "A document type is required."));
        }

        if (startingNumber < 0)
        {
            return Result.Failure<NumberingSeries>(Error.Validation(
                "NumberingSeries.StartingNumberNegative",
                "A starting number cannot be negative."));
        }

        if (numberLength is < 1 or > MaximumNumberLength)
        {
            return Result.Failure<NumberingSeries>(Error.Validation(
                "NumberingSeries.NumberLengthOutOfRange",
                $"A number length must be between 1 and {MaximumNumberLength}, but " +
                $"{numberLength} was supplied."));
        }

        return Result.Success(new NumberingSeries(
            NumberingSeriesId.NewId(), tenantId, firmId, branchId, financialYearId,
            documentType.Trim().ToLowerInvariant(), startingNumber, numberLength));
    }

    /// <summary>Sets how the number is assembled.</summary>
    /// <param name="prefix">Text before the number.</param>
    /// <param name="suffix">Text after the number.</param>
    /// <param name="separator">The segment separator.</param>
    /// <param name="financialYearLabel">
    /// The year label to include, or <see langword="null"/> to omit the year segment.
    /// </param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetFormat(
        string? prefix,
        string? suffix = null,
        string? separator = null,
        string? financialYearLabel = null)
    {
        if (prefix?.Length > 20 || suffix?.Length > 20)
        {
            return Result.Failure(Error.Validation(
                "NumberingSeries.AffixTooLong",
                "A prefix or suffix cannot exceed 20 characters."));
        }

        if (separator?.Length > 5)
        {
            return Result.Failure(Error.Validation(
                "NumberingSeries.SeparatorTooLong",
                "A separator cannot exceed 5 characters."));
        }

        Prefix = Trimmed(prefix);
        Suffix = Trimmed(suffix);
        Separator = separator ?? string.Empty;
        FinancialYearLabel = Trimmed(financialYearLabel);

        return Result.Success();
    }

    /// <summary>Formats a specific counter value without consuming it.</summary>
    /// <param name="number">The counter value.</param>
    /// <returns>The formatted document number.</returns>
    /// <remarks>
    /// Separate from <see cref="Reserve"/> so a screen can preview the next number
    /// without burning it. Previewing by reserving would leave a gap in the
    /// sequence every time a user opened a form and thought better of it - and gaps
    /// are exactly what an audit asks about.
    /// </remarks>
    public string Format(int number)
    {
        string padded = number.ToString(
            CultureInfo.InvariantCulture).PadLeft(NumberLength, '0');

        List<string> segments = [];

        if (!string.IsNullOrEmpty(Prefix))
        {
            segments.Add(Prefix);
        }

        if (!string.IsNullOrEmpty(FinancialYearLabel))
        {
            segments.Add(FinancialYearLabel);
        }

        segments.Add(padded);

        if (!string.IsNullOrEmpty(Suffix))
        {
            segments.Add(Suffix);
        }

        return string.Join(Separator, segments);
    }

    /// <summary>Gets the number that would be issued next, without consuming it.</summary>
    /// <returns>The formatted next number.</returns>
    public string Peek() => Format(NextNumber);

    /// <summary>Consumes the next number and advances the counter.</summary>
    /// <returns>The formatted number, or a failure.</returns>
    /// <remarks>
    /// <para>
    /// Concurrency is handled at two levels, because neither alone is enough. The
    /// series is an aggregate root with an <c>xmin</c> concurrency token, so two
    /// transactions reserving at once produce a
    /// <c>DbUpdateConcurrencyException</c> for the loser rather than issuing the
    /// same number twice. A unique index on the document number backs that up, so
    /// even a defect in the reservation path cannot land two documents sharing a
    /// number.
    /// </para>
    /// <para>
    /// The caller is expected to retry on conflict. Reserving inside the same
    /// transaction as the document it numbers means an aborted save also releases
    /// the number, which is what keeps the sequence free of gaps.
    /// </para>
    /// </remarks>
    public Result<string> Reserve()
    {
        if (!IsActive)
        {
            return Result.Failure<string>(Error.BusinessRule(
                "NumberingSeries.Inactive",
                $"The numbering series for '{DocumentType}' is inactive."));
        }

        // Guards against a series silently wrapping round and reissuing numbers it
        // has already used. Widening NumberLength is a configuration change; reusing
        // a document number is a corrupted audit trail.
        int capacity = (int)Math.Pow(10, NumberLength);

        if (NextNumber >= capacity)
        {
            return Result.Failure<string>(Error.BusinessRule(
                "NumberingSeries.Exhausted",
                $"The numbering series for '{DocumentType}' has reached its limit of " +
                $"{NumberLength} digits. Increase the number length to continue."));
        }

        string formatted = Format(NextNumber);
        NextNumber++;

        return Result.Success(formatted);
    }

    /// <summary>Resets the counter, for example when opening a new financial year.</summary>
    /// <param name="startingNumber">The number to restart from.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Reset(int startingNumber)
    {
        if (startingNumber < 0)
        {
            return Result.Failure(Error.Validation(
                "NumberingSeries.StartingNumberNegative",
                "A starting number cannot be negative."));
        }

        StartingNumber = startingNumber;
        NextNumber = startingNumber;

        return Result.Success();
    }

    /// <summary>Widens the counter so an exhausted series can continue.</summary>
    /// <param name="numberLength">The new digit count.</param>
    /// <returns>Success, or a validation failure.</returns>
    /// <remarks>
    /// Narrowing is refused. Shortening the length would reformat every number the
    /// series is about to issue into a shape that may already exist.
    /// </remarks>
    public Result WidenTo(int numberLength)
    {
        if (numberLength > MaximumNumberLength)
        {
            return Result.Failure(Error.Validation(
                "NumberingSeries.NumberLengthOutOfRange",
                $"A number length cannot exceed {MaximumNumberLength}."));
        }

        if (numberLength < NumberLength)
        {
            return Result.Failure(Error.BusinessRule(
                "NumberingSeries.CannotNarrow",
                $"The number length cannot be reduced from {NumberLength} to " +
                $"{numberLength}: existing numbers would no longer be reproducible."));
        }

        NumberLength = numberLength;

        return Result.Success();
    }

    /// <summary>Stops the series issuing further numbers.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows the series to issue numbers again.</summary>
    public void Activate() => IsActive = true;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The document types the shipped screens number.
/// </summary>
/// <remarks>
/// Constants rather than an enum, because <see cref="NumberingSeries.DocumentType"/>
/// is a string so administrators can configure numbering for document types the
/// shipped code does not know about.
/// </remarks>
public static class DocumentTypes
{
    /// <summary>A cash receipt voucher.</summary>
    public const string CashReceipt = "accounting.cash-receipt";

    /// <summary>A bank receipt voucher.</summary>
    public const string BankReceipt = "accounting.bank-receipt";

    /// <summary>A cash payment voucher.</summary>
    public const string CashPayment = "accounting.cash-payment";

    /// <summary>A bank payment voucher.</summary>
    public const string BankPayment = "accounting.bank-payment";

    /// <summary>A journal voucher.</summary>
    public const string Journal = "accounting.journal";

    /// <summary>A contra voucher.</summary>
    public const string Contra = "accounting.contra";

    /// <summary>An opening-balance voucher.</summary>
    public const string OpeningBalance = "accounting.opening-balance";

    /// <summary>A sales quotation.</summary>
    public const string SalesQuotation = "sales.quotation";

    /// <summary>A sales order.</summary>
    public const string SalesOrder = "sales.order";

    /// <summary>A delivery note.</summary>
    public const string DeliveryNote = "sales.delivery-note";

    /// <summary>A sales invoice.</summary>
    public const string SalesInvoice = "sales.invoice";

    /// <summary>A sales return.</summary>
    public const string SalesReturn = "sales.return";

    /// <summary>A purchase order.</summary>
    public const string PurchaseOrder = "purchase.order";

    /// <summary>A purchase invoice.</summary>
    public const string PurchaseInvoice = "purchase.invoice";

    /// <summary>A purchase return.</summary>
    public const string PurchaseReturn = "purchase.return";

    /// <summary>A service job card.</summary>
    public const string JobCard = "service.job-card";

    /// <summary>An opening-stock document.</summary>
    public const string OpeningStock = "inventory.opening-stock";

    /// <summary>A material receipt.</summary>
    public const string MaterialReceipt = "inventory.material-receipt";

    /// <summary>A material issue.</summary>
    public const string MaterialIssue = "inventory.material-issue";

    /// <summary>A stock transfer between warehouses.</summary>
    public const string StockTransfer = "inventory.stock-transfer";

    /// <summary>A stock adjustment.</summary>
    public const string StockAdjustment = "inventory.stock-adjustment";

    /// <summary>A damaged-stock write-off.</summary>
    public const string DamagedStock = "inventory.damaged-stock";

    /// <summary>A physical stock verification.</summary>
    public const string PhysicalVerification = "inventory.physical-verification";

    /// <summary>Maps a voucher type onto its document type.</summary>
    /// <param name="type">The voucher type.</param>
    /// <returns>The document-type key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for an unrecognised type.</exception>
    public static string ForVoucher(Accounting.VoucherType type) => type switch
    {
        Accounting.VoucherType.CashReceipt => CashReceipt,
        Accounting.VoucherType.BankReceipt => BankReceipt,
        Accounting.VoucherType.CashPayment => CashPayment,
        Accounting.VoucherType.BankPayment => BankPayment,
        Accounting.VoucherType.Journal => Journal,
        Accounting.VoucherType.Contra => Contra,
        Accounting.VoucherType.OpeningBalance => OpeningBalance,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "No document type is mapped for this voucher type."),
    };

    /// <summary>Maps a stock operation onto its document type.</summary>
    /// <param name="type">The stock document type.</param>
    /// <returns>The document-type key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for an unrecognised type.</exception>
    /// <remarks>
    /// A series each rather than one shared inventory sequence. A firm reconciling a
    /// month of transfers should not have to read past its issues to do it, and the
    /// reference application numbers them separately for the same reason.
    /// </remarks>
    public static string ForStockDocument(Inventory.StockDocumentType type) => type switch
    {
        Inventory.StockDocumentType.OpeningStock => OpeningStock,
        Inventory.StockDocumentType.MaterialReceipt => MaterialReceipt,
        Inventory.StockDocumentType.MaterialIssue => MaterialIssue,
        Inventory.StockDocumentType.StockTransfer => StockTransfer,
        Inventory.StockDocumentType.StockAdjustment => StockAdjustment,
        Inventory.StockDocumentType.DamagedStock => DamagedStock,
        Inventory.StockDocumentType.PhysicalVerification => PhysicalVerification,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "No document type is mapped for this stock operation."),
    };
}
