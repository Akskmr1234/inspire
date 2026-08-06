using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using FluentValidation;

namespace ERP.Application.Accounting.Vouchers;

/// <summary>One line of a voucher being created.</summary>
/// <param name="LedgerId">The ledger to post against.</param>
/// <param name="Side">Whether the line debits or credits that ledger.</param>
/// <param name="Amount">The amount, in the entry currency. Must be positive.</param>
/// <param name="Narration">An optional line narration.</param>
/// <param name="BillReferences">
/// The bills this line raises or settles, for a posting against a bill-wise ledger.
/// Supplied, they must account for the whole line; omitted, the posting simply moves
/// the party's balance without touching any bill.
/// </param>
public sealed record CreateVoucherLine(
    Guid LedgerId,
    EntrySide Side,
    decimal Amount,
    string? Narration = null,
    IReadOnlyList<CreateVoucherBillReference>? BillReferences = null);

/// <summary>Creates a voucher and posts it to the ledgers.</summary>
/// <param name="Type">The kind of voucher.</param>
/// <param name="Date">The document date.</param>
/// <param name="Lines">The postings. At least two, balancing.</param>
/// <param name="CurrencyCode">The entry currency. Defaults to the firm's base currency.</param>
/// <param name="ExchangeRate">The rate to the base currency. Must be 1 when they match.</param>
/// <param name="ReferenceNumber">A related reference or invoice number.</param>
/// <param name="Narration">The voucher narration.</param>
/// <param name="PaymentMode">The payment mode, for a receipt or payment.</param>
/// <param name="PostImmediately">
/// Whether to post on creation, or leave the voucher as an editable draft.
/// </param>
/// <remarks>
/// Creating and posting are one command because that is how the entry screen
/// behaves - the user presses Save and expects the entry to be in the books.
/// <see cref="PostImmediately"/> exists for the workflow where a clerk enters and a
/// supervisor posts.
/// </remarks>
public sealed record CreateVoucherCommand(
    VoucherType Type,
    DateOnly Date,
    IReadOnlyList<CreateVoucherLine> Lines,
    string? CurrencyCode = null,
    decimal ExchangeRate = 1m,
    string? ReferenceNumber = null,
    string? Narration = null,
    string? PaymentMode = null,
    bool PostImmediately = true) : ICommand<CreateVoucherResponse>, ITransactional;

/// <summary>The created voucher.</summary>
/// <param name="VoucherId">The new voucher.</param>
/// <param name="Number">The document number issued by the numbering series.</param>
/// <param name="Status">Whether it was left as a draft or posted.</param>
/// <param name="TotalDebit">The voucher total, in the entry currency.</param>
public sealed record CreateVoucherResponse(
    Guid VoucherId,
    string Number,
    VoucherStatus Status,
    decimal TotalDebit);

/// <summary>Validates the shape of a <see cref="CreateVoucherCommand"/>.</summary>
/// <remarks>
/// Shape only. Whether the voucher actually balances is a domain invariant enforced
/// by <see cref="Voucher.Post"/>, not duplicated here - two copies of that rule
/// would eventually disagree, and the domain's copy is the one that cannot be
/// bypassed.
/// </remarks>
public sealed class CreateVoucherCommandValidator : AbstractValidator<CreateVoucherCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateVoucherCommandValidator"/> class.</summary>
    public CreateVoucherCommandValidator()
    {
        RuleFor(c => c.Type).IsInEnum();

        RuleFor(c => c.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("A document date is required.");

        RuleFor(c => c.Lines)
            .NotEmpty()
            .WithMessage("A voucher needs at least two lines.")
            .Must(l => l.Count >= 2)
            .WithMessage("A voucher needs at least two lines to balance.");

        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.LedgerId)
                .NotEqual(Guid.Empty)
                .WithMessage("Each line must name a ledger.");

            line.RuleFor(l => l.Amount)
                .GreaterThan(0m)
                .WithMessage("Each line amount must be greater than zero.");

            line.RuleFor(l => l.Side).IsInEnum();

            line.RuleFor(l => l.Narration).MaximumLength(500);

            // Shape only, again. Whether the references account for the line, and
            // whether the ledger is even tracked bill-wise, needs the ledger loaded -
            // so those live with the posting rules rather than here.
            line.RuleForEach(l => l.BillReferences!)
                .ChildRules(reference =>
                {
                    reference.RuleFor(r => r.Kind).IsInEnum();

                    reference.RuleFor(r => r.Amount)
                        .GreaterThan(0m)
                        .WithMessage("Each bill reference must be for a positive amount.");

                    reference.RuleFor(r => r.BillNumber).MaximumLength(50);

                    reference.RuleFor(r => r.CreditDays)
                        .GreaterThanOrEqualTo(0)
                        .When(r => r.CreditDays.HasValue)
                        .WithMessage("A credit period cannot be negative.");
                })
                .When(l => l.BillReferences is not null);
        });

        RuleFor(c => c.ExchangeRate).GreaterThan(0m);
        RuleFor(c => c.CurrencyCode).Length(3).When(c => c.CurrencyCode is not null);
        RuleFor(c => c.ReferenceNumber).MaximumLength(50);
        RuleFor(c => c.Narration).MaximumLength(2000);
        RuleFor(c => c.PaymentMode).MaximumLength(30);
    }
}

/// <summary>Handles <see cref="CreateVoucherCommand"/>.</summary>
public sealed class CreateVoucherCommandHandler
    : ICommandHandler<CreateVoucherCommand, CreateVoucherResponse>
{
    private readonly IVoucherRepository _vouchers;
    private readonly ILedgerRepository _ledgers;
    private readonly IFinancialYearRepository _financialYears;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly VoucherBillReferencePoster _billReferences;

    /// <summary>Initialises a new instance of the <see cref="CreateVoucherCommandHandler"/> class.</summary>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="bills">The bill repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateVoucherCommandHandler(
        IVoucherRepository vouchers,
        ILedgerRepository ledgers,
        IBillRepository bills,
        IFinancialYearRepository financialYears,
        INumberingSeriesRepository numbering,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _vouchers = vouchers;
        _ledgers = ledgers;
        _financialYears = financialYears;
        _numbering = numbering;
        _firms = firms;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _billReferences = new VoucherBillReferencePoster(bills);
    }

    /// <inheritdoc />
    public async Task<Result<CreateVoucherResponse>> Handle(
        CreateVoucherCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<CreateVoucherResponse>(Error.Forbidden(
                "Voucher.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before posting a voucher."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<CreateVoucherResponse>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<CreateVoucherResponse>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}. Create one before " +
                $"posting to that date."));
        }

        Result<CurrencyCode> currency = request.CurrencyCode is null
            ? Result.Success(firm.BaseCurrency)
            : CurrencyCode.Create(request.CurrencyCode);

        if (currency.IsFailure)
        {
            return Result.Failure<CreateVoucherResponse>(currency.Error);
        }

        // Every referenced ledger is loaded up front, in one query, and checked
        // before anything is built. Discovering a bad ledger halfway through
        // assembling the voucher would leave a reserved number burnt for nothing.
        Result<IReadOnlyDictionary<LedgerId, Ledger>> ledgers =
            await LoadAndValidateLedgersAsync(request, firmId, cancellationToken);

        if (ledgers.IsFailure)
        {
            return Result.Failure<CreateVoucherResponse>(ledgers.Error);
        }

        Result<string> number = await ReserveNumberAsync(
            request.Type, firmId, branchId, year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<CreateVoucherResponse>(number.Error);
        }

        Result<Voucher> draft = Voucher.CreateDraft(
            _tenantContext.TenantId, firmId, branchId, year, request.Type,
            number.Value, request.Date, currency.Value, firm.BaseCurrency,
            request.ExchangeRate);

        if (draft.IsFailure)
        {
            return Result.Failure<CreateVoucherResponse>(draft.Error);
        }

        Voucher voucher = draft.Value;

        foreach (CreateVoucherLine line in request.Lines)
        {
            Result<VoucherLine> added = voucher.AddLine(
                LedgerId.From(line.LedgerId), line.Side, line.Amount, line.Narration);

            if (added.IsFailure)
            {
                return Result.Failure<CreateVoucherResponse>(added.Error);
            }
        }

        Result details = voucher.SetDetails(
            request.ReferenceNumber, request.Narration, request.PaymentMode);

        if (details.IsFailure)
        {
            return Result.Failure<CreateVoucherResponse>(details.Error);
        }

        if (request.PostImmediately)
        {
            // The balance invariant is enforced here, in the domain. If it fails the
            // transaction rolls back, which also releases the reserved number - so a
            // rejected posting leaves no gap in the sequence.
            Result posted = voucher.Post(_currentUser.UserId, _clock.UtcNow);

            if (posted.IsFailure)
            {
                return Result.Failure<CreateVoucherResponse>(posted.Error);
            }
        }

        // After posting, so a draft's references are refused rather than acted on,
        // and before the save, so the voucher and the settlements it makes commit
        // together. The command is ITransactional; a failure here rolls the posting
        // back with it.
        Result settled = await _billReferences.ApplyAsync(
            voucher, request.Lines, ledgers.Value, cancellationToken);

        if (settled.IsFailure)
        {
            return Result.Failure<CreateVoucherResponse>(settled.Error);
        }

        _vouchers.Add(voucher);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateVoucherResponse(
            voucher.Id.Value,
            voucher.Number,
            voucher.Status,
            voucher.TotalDebit.Amount));
    }

    private async Task<Result<IReadOnlyDictionary<LedgerId, Ledger>>> LoadAndValidateLedgersAsync(
        CreateVoucherCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        List<LedgerId> ids = [.. request.Lines.Select(l => LedgerId.From(l.LedgerId)).Distinct()];

        IReadOnlyDictionary<LedgerId, Ledger> ledgers =
            await _ledgers.GetManyAsync(ids, cancellationToken);

        foreach (LedgerId id in ids)
        {
            if (!ledgers.TryGetValue(id, out Ledger? ledger))
            {
                return Result.Failure<IReadOnlyDictionary<LedgerId, Ledger>>(Error.NotFound(
                    "Ledger.NotFound", $"Ledger {id} does not exist."));
            }

            // The tenant filter already prevents reading another tenant's ledger.
            // This catches the subtler case: a ledger belonging to a different firm
            // within the same tenant, which would post into the wrong set of books.
            if (ledger.FirmId != firmId)
            {
                return Result.Failure<IReadOnlyDictionary<LedgerId, Ledger>>(Error.Validation(
                    "Ledger.WrongFirm",
                    $"Ledger '{ledger.Name}' belongs to a different firm."));
            }

            Result postable = ledger.EnsurePostable();

            if (postable.IsFailure)
            {
                return Result.Failure<IReadOnlyDictionary<LedgerId, Ledger>>(postable.Error);
            }
        }

        return Result.Success(ledgers);
    }

    /// <summary>
    /// Takes the next document number, creating a default series if none is
    /// configured.
    /// </summary>
    /// <remarks>
    /// Auto-creating the series rather than failing is deliberate. A fresh
    /// installation has no series configured, and refusing to post until an
    /// administrator visits a settings screen makes the system look broken. The
    /// default is a plain four-digit sequence per branch and year, which an
    /// administrator can then reshape.
    /// </remarks>
    private async Task<Result<string>> ReserveNumberAsync(
        VoucherType type,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        string documentType = DocumentTypes.ForVoucher(type);

        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            documentType, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, documentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat(
                prefix: DefaultPrefix(type),
                suffix: null,
                separator: "/",
                financialYearLabel: year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }

    private static string DefaultPrefix(VoucherType type) => type switch
    {
        VoucherType.CashReceipt => "CR",
        VoucherType.BankReceipt => "BR",
        VoucherType.CashPayment => "CP",
        VoucherType.BankPayment => "BP",
        VoucherType.Journal => "JV",
        VoucherType.Contra => "CN",
        VoucherType.OpeningBalance => "OB",
        _ => "VC",
    };
}
