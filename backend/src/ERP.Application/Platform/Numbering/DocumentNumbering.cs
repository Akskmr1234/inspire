using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Platform.Numbering;

/// <summary>Lists how each document a user enters is numbered, for one date.</summary>
/// <param name="Date">The date the numbers would be issued on; it decides the year.</param>
public sealed record ListDocumentNumberingQuery(DateOnly Date)
    : IQuery<IReadOnlyList<DocumentNumberingView>>;

/// <summary>The number a document of one type would be given next.</summary>
/// <param name="DocumentType">A value from <see cref="DocumentTypes"/>.</param>
/// <param name="Date">The document's date; it decides the year.</param>
public sealed record PeekDocumentNumberQuery(string DocumentType, DateOnly Date)
    : IQuery<DocumentNumberingView>;

/// <summary>Changes how a document type is numbered from here on.</summary>
/// <param name="DocumentType">A value from <see cref="DocumentTypes"/>.</param>
/// <param name="Date">A date in the year being configured.</param>
/// <param name="Prefix">Text before the number, or <see langword="null"/> for none.</param>
/// <param name="Suffix">Text after the number, or <see langword="null"/> for none.</param>
/// <param name="Separator">What joins the segments.</param>
/// <param name="IncludeYear">Whether the financial year appears in the number.</param>
/// <param name="NextNumber">
/// The counter to continue from, or <see langword="null"/> to leave it where it is.
/// </param>
public sealed record SetDocumentNumberingCommand(
    string DocumentType,
    DateOnly Date,
    string? Prefix,
    string? Suffix,
    string Separator,
    bool IncludeYear,
    int? NextNumber) : ICommand<DocumentNumberingView>, ITransactional;

/// <summary>How one document type is numbered.</summary>
/// <param name="DocumentType">The document type.</param>
/// <param name="Prefix">The prefix.</param>
/// <param name="Suffix">The suffix.</param>
/// <param name="Separator">The separator.</param>
/// <param name="IncludeYear">Whether the year appears in the number.</param>
/// <param name="NextNumber">The counter's next value.</param>
/// <param name="Next">The next number, formatted, as it would be issued.</param>
/// <param name="IsConfigured">
/// Whether a series exists yet. Until the first document of the type is saved, the
/// format shown is the default the first save will use.
/// </param>
/// <param name="YearLabel">
/// The financial year's label, which is what appears in the number when the year is
/// included - so a screen can preview a format before it is saved.
/// </param>
public sealed record DocumentNumberingView(
    string DocumentType,
    string? Prefix,
    string? Suffix,
    string Separator,
    bool IncludeYear,
    int NextNumber,
    string Next,
    bool IsConfigured,
    string YearLabel);

/// <summary>Validates <see cref="SetDocumentNumberingCommand"/>.</summary>
public sealed class SetDocumentNumberingCommandValidator
    : AbstractValidator<SetDocumentNumberingCommand>
{
    /// <summary>Initialises a new instance of the <see cref="SetDocumentNumberingCommandValidator"/> class.</summary>
    public SetDocumentNumberingCommandValidator()
    {
        RuleFor(c => c.DocumentType).NotEmpty();
        RuleFor(c => c.Prefix).MaximumLength(20);
        RuleFor(c => c.Suffix).MaximumLength(20);
        RuleFor(c => c.Separator).NotNull().MaximumLength(5);
        RuleFor(c => c.NextNumber).GreaterThanOrEqualTo(1).When(c => c.NextNumber is not null);
    }
}

/// <summary>The document types a firm configures numbering for, in the order shown.</summary>
public static class NumberedDocuments
{
    /// <summary>Gets the types a user enters directly and therefore sees numbered.</summary>
    /// <remarks>
    /// The stock movements a commercial document posts behind it are left out on
    /// purpose: nobody types those, and offering their prefixes here would put a dozen
    /// rows between somebody and the invoice number they came to change.
    /// </remarks>
    public static IReadOnlyList<string> UserFacing { get; } =
    [
        DocumentTypes.SalesInvoice,
        DocumentTypes.SalesReturn,
        DocumentTypes.SalesOrder,
        DocumentTypes.PurchaseInvoice,
        DocumentTypes.PurchaseReturn,
        DocumentTypes.PurchaseOrder,
        DocumentTypes.CashReceipt,
        DocumentTypes.BankReceipt,
        DocumentTypes.CashPayment,
        DocumentTypes.BankPayment,
        DocumentTypes.Journal,
    ];

    /// <summary>Whether a type is one this API configures.</summary>
    /// <param name="documentType">The type.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsKnown(string documentType) =>
        UserFacing.Contains(documentType, StringComparer.Ordinal);
}

/// <summary>Reads the series that would number a document, without changing it.</summary>
internal static class NumberingLookup
{
    /// <summary>Describes the series for a type, or the default it would start as.</summary>
    /// <param name="series">The series, if one exists.</param>
    /// <param name="documentType">The type.</param>
    /// <param name="year">The financial year.</param>
    /// <returns>The view.</returns>
    internal static DocumentNumberingView Describe(
        NumberingSeries? series,
        string documentType,
        FinancialYear year)
    {
        if (series is not null)
        {
            return new DocumentNumberingView(
                documentType,
                series.Prefix,
                series.Suffix,
                series.Separator,
                series.FinancialYearLabel is not null,
                series.NextNumber,
                series.Peek(),
                IsConfigured: true,
                year.Code);
        }

        // What the first save of this type will create, so the screen can show the
        // number the document will actually get instead of inventing one.
        Result<NumberingSeries> preview = NumberingSeries.Create(
            year.TenantId, year.FirmId, documentType);

        NumberingSeries shaped = preview.Value;
        NumberingDefaults.Apply(shaped, year.Code);

        return new DocumentNumberingView(
            documentType,
            shaped.Prefix,
            shaped.Suffix,
            shaped.Separator,
            IncludeYear: true,
            shaped.NextNumber,
            shaped.Peek(),
            IsConfigured: false,
            year.Code);
    }
}

/// <summary>Handles <see cref="ListDocumentNumberingQuery"/>.</summary>
public sealed class ListDocumentNumberingQueryHandler
    : IQueryHandler<ListDocumentNumberingQuery, IReadOnlyList<DocumentNumberingView>>
{
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _years;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ListDocumentNumberingQueryHandler"/> class.</summary>
    /// <param name="numbering">The numbering series repository.</param>
    /// <param name="years">The financial year repository.</param>
    /// <param name="tenantContext">The ambient tenant.</param>
    public ListDocumentNumberingQueryHandler(
        INumberingSeriesRepository numbering,
        IFinancialYearRepository years,
        ITenantContext tenantContext)
    {
        _numbering = numbering;
        _years = years;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DocumentNumberingView>>> Handle(
        ListDocumentNumberingQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<IReadOnlyList<DocumentNumberingView>>(Error.Forbidden(
                "Numbering.NoFirmOrBranchSelected",
                "A firm and a branch must be selected to read their numbering."));
        }

        FinancialYear? year = await _years.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<IReadOnlyList<DocumentNumberingView>>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}."));
        }

        List<DocumentNumberingView> views = [];

        foreach (string documentType in NumberedDocuments.UserFacing)
        {
            NumberingSeries? series = await _numbering.FindForUpdateAsync(
                documentType, firmId, branchId, year.Id, cancellationToken);

            views.Add(NumberingLookup.Describe(series, documentType, year));
        }

        return Result.Success<IReadOnlyList<DocumentNumberingView>>(views);
    }
}

/// <summary>Handles <see cref="PeekDocumentNumberQuery"/>.</summary>
public sealed class PeekDocumentNumberQueryHandler
    : IQueryHandler<PeekDocumentNumberQuery, DocumentNumberingView>
{
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _years;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="PeekDocumentNumberQueryHandler"/> class.</summary>
    /// <param name="numbering">The numbering series repository.</param>
    /// <param name="years">The financial year repository.</param>
    /// <param name="tenantContext">The ambient tenant.</param>
    public PeekDocumentNumberQueryHandler(
        INumberingSeriesRepository numbering,
        IFinancialYearRepository years,
        ITenantContext tenantContext)
    {
        _numbering = numbering;
        _years = years;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads without reserving. Previewing by reserving would leave a gap in the
    /// sequence each time somebody opened a form and closed it again, and gaps are
    /// exactly what an audit asks about - so the number shown is the one the document
    /// gets unless somebody else saves first.
    /// </remarks>
    public async Task<Result<DocumentNumberingView>> Handle(
        PeekDocumentNumberQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NumberedDocuments.IsKnown(request.DocumentType))
        {
            return Result.Failure<DocumentNumberingView>(Error.Validation(
                "Numbering.UnknownDocumentType",
                $"'{request.DocumentType}' is not a document type that is numbered here."));
        }

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<DocumentNumberingView>(Error.Forbidden(
                "Numbering.NoFirmOrBranchSelected",
                "A firm and a branch must be selected to read their numbering."));
        }

        FinancialYear? year = await _years.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<DocumentNumberingView>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}."));
        }

        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            request.DocumentType, firmId, branchId, year.Id, cancellationToken);

        return Result.Success(NumberingLookup.Describe(series, request.DocumentType, year));
    }
}

/// <summary>Handles <see cref="SetDocumentNumberingCommand"/>.</summary>
public sealed class SetDocumentNumberingCommandHandler
    : ICommandHandler<SetDocumentNumberingCommand, DocumentNumberingView>
{
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _years;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetDocumentNumberingCommandHandler"/> class.</summary>
    /// <param name="numbering">The numbering series repository.</param>
    /// <param name="years">The financial year repository.</param>
    /// <param name="tenantContext">The ambient tenant.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetDocumentNumberingCommandHandler(
        INumberingSeriesRepository numbering,
        IFinancialYearRepository years,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _numbering = numbering;
        _years = years;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Changes the series the next document of this type would take its number from,
    /// creating it - for this branch and year, as the first save would - where none
    /// exists yet. Numbers already issued are never touched: they are printed on
    /// documents that have left the building.
    /// </para>
    /// <para>
    /// The counter may move forward and never back. Moving it back would reissue a
    /// number a document already carries, and the unique index would then refuse the
    /// next save with a message about an index rather than about numbering.
    /// </para>
    /// </remarks>
    public async Task<Result<DocumentNumberingView>> Handle(
        SetDocumentNumberingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NumberedDocuments.IsKnown(request.DocumentType))
        {
            return Result.Failure<DocumentNumberingView>(Error.Validation(
                "Numbering.UnknownDocumentType",
                $"'{request.DocumentType}' is not a document type that is numbered here."));
        }

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<DocumentNumberingView>(Error.Forbidden(
                "Numbering.NoFirmOrBranchSelected",
                "A firm and a branch must be selected to change their numbering."));
        }

        FinancialYear? year = await _years.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<DocumentNumberingView>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}."));
        }

        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            request.DocumentType, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, request.DocumentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<DocumentNumberingView>(created.Error);
            }

            series = created.Value;
            _numbering.Add(series);
        }

        Result formatted = series.SetFormat(
            request.Prefix,
            request.Suffix,
            request.Separator,
            request.IncludeYear ? year.Code : null);

        if (formatted.IsFailure)
        {
            return Result.Failure<DocumentNumberingView>(formatted.Error);
        }

        if (request.NextNumber is { } next && next != series.NextNumber)
        {
            if (next < series.NextNumber)
            {
                return Result.Failure<DocumentNumberingView>(Error.BusinessRule(
                    "Numbering.CannotMoveBack",
                    $"The next number cannot go back from {series.NextNumber} to {next}: "
                    + "numbers already issued would be issued again."));
            }

            Result reset = series.Reset(next);

            if (reset.IsFailure)
            {
                return Result.Failure<DocumentNumberingView>(reset.Error);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(NumberingLookup.Describe(series, request.DocumentType, year));
    }
}
