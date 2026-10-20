using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Platform.Settings;

/// <summary>A financial year, as the settings screen lists it.</summary>
/// <param name="FinancialYearId">The year.</param>
/// <param name="Code">Its label, such as <c>2026</c> or <c>2026-27</c>.</param>
/// <param name="StartDate">The first date, inclusive.</param>
/// <param name="EndDate">The last date, inclusive.</param>
/// <param name="Status">Whether it takes postings, is closed, or is locked for good.</param>
public sealed record FinancialYearSummary(
    Guid FinancialYearId,
    string Code,
    DateOnly StartDate,
    DateOnly EndDate,
    FinancialYearStatus Status);

/// <summary>Lists the current firm's financial years, earliest first.</summary>
public sealed record ListFinancialYearsQuery : IQuery<IReadOnlyList<FinancialYearSummary>>;

/// <summary>Adds a financial year.</summary>
/// <param name="Code">Its label.</param>
/// <param name="StartDate">The first date, inclusive.</param>
/// <param name="EndDate">The last date, inclusive.</param>
public sealed record CreateFinancialYearCommand(string Code, DateOnly StartDate, DateOnly EndDate)
    : ICommand<FinancialYearSummary>;

/// <summary>Renames a financial year or moves its dates.</summary>
/// <param name="FinancialYearId">The year.</param>
/// <param name="Code">Its label.</param>
/// <param name="StartDate">The first date, inclusive.</param>
/// <param name="EndDate">The last date, inclusive.</param>
public sealed record UpdateFinancialYearCommand(
    Guid FinancialYearId,
    string Code,
    DateOnly StartDate,
    DateOnly EndDate) : ICommand<FinancialYearSummary>;

/// <summary>Closes a year to postings, or opens it again.</summary>
/// <param name="FinancialYearId">The year.</param>
/// <param name="Open">Whether it should take postings.</param>
public sealed record SetFinancialYearOpenCommand(Guid FinancialYearId, bool Open)
    : ICommand<FinancialYearSummary>;

/// <summary>Validates <see cref="CreateFinancialYearCommand"/>.</summary>
public sealed class CreateFinancialYearCommandValidator : AbstractValidator<CreateFinancialYearCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateFinancialYearCommandValidator"/> class.</summary>
    public CreateFinancialYearCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(20);
        RuleFor(c => c.EndDate).GreaterThanOrEqualTo(c => c.StartDate)
            .WithMessage("A year cannot end before it starts.");
    }
}

/// <summary>Validates <see cref="UpdateFinancialYearCommand"/>.</summary>
public sealed class UpdateFinancialYearCommandValidator : AbstractValidator<UpdateFinancialYearCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateFinancialYearCommandValidator"/> class.</summary>
    public UpdateFinancialYearCommandValidator()
    {
        RuleFor(c => c.FinancialYearId).NotEqual(Guid.Empty);
        RuleFor(c => c.Code).NotEmpty().MaximumLength(20);
        RuleFor(c => c.EndDate).GreaterThanOrEqualTo(c => c.StartDate)
            .WithMessage("A year cannot end before it starts.");
    }
}

/// <summary>Handles the financial-year queries and commands.</summary>
/// <remarks>
/// A year is the period every document posts into, so it is set up here rather than
/// assumed: Gulf firms run calendar years, Indian firms April to March, and a firm moving
/// from another system may have a first year of any length.
/// </remarks>
public sealed class FinancialYearHandlers
    : IQueryHandler<ListFinancialYearsQuery, IReadOnlyList<FinancialYearSummary>>,
      ICommandHandler<CreateFinancialYearCommand, FinancialYearSummary>,
      ICommandHandler<UpdateFinancialYearCommand, FinancialYearSummary>,
      ICommandHandler<SetFinancialYearOpenCommand, FinancialYearSummary>
{
    private readonly IFinancialYearRepository _years;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="FinancialYearHandlers"/> class.</summary>
    /// <param name="years">The financial-year repository.</param>
    /// <param name="tenantContext">The ambient tenant.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public FinancialYearHandlers(
        IFinancialYearRepository years,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _years = years;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FinancialYearSummary>>> Handle(
        ListFinancialYearsQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<FinancialYearSummary>>(NoFirm());
        }

        IReadOnlyList<FinancialYear> years = await _years.ListAsync(firmId, cancellationToken);

        return Result.Success<IReadOnlyList<FinancialYearSummary>>([.. years.Select(Describe)]);
    }

    /// <inheritdoc />
    public async Task<Result<FinancialYearSummary>> Handle(
        CreateFinancialYearCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<FinancialYearSummary>(NoFirm());
        }

        IReadOnlyList<FinancialYear> existing = await _years.ListAsync(firmId, cancellationToken);

        Result<FinancialYear> created = FinancialYear.Create(
            _tenantContext.TenantId, firmId, request.Code, request.StartDate, request.EndDate,
            existing);

        if (created.IsFailure)
        {
            return Result.Failure<FinancialYearSummary>(created.Error);
        }

        _years.Add(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(created.Value));
    }

    /// <inheritdoc />
    public async Task<Result<FinancialYearSummary>> Handle(
        UpdateFinancialYearCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FinancialYear> found = await FindAsync(request.FinancialYearId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<FinancialYearSummary>(found.Error);
        }

        FinancialYear year = found.Value;

        // Moving the dates must not strand a document outside the year it was posted into:
        // its period, its numbering and every balance derived from it are that year's.
        int stranded = await _years.CountDocumentsOutsideAsync(
            year.Id, request.StartDate, request.EndDate, cancellationToken);

        if (stranded > 0)
        {
            return Result.Failure<FinancialYearSummary>(Error.BusinessRule(
                "FinancialYear.DocumentsOutside",
                $"{stranded} document(s) of '{year.Code}' are dated outside "
                + $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}. "
                + "Choose dates that still cover them."));
        }

        IReadOnlyList<FinancialYear> all = await _years.ListAsync(year.FirmId, cancellationToken);

        Result moved = year.Reschedule(request.Code, request.StartDate, request.EndDate, all);

        if (moved.IsFailure)
        {
            return Result.Failure<FinancialYearSummary>(moved.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(year));
    }

    /// <inheritdoc />
    public async Task<Result<FinancialYearSummary>> Handle(
        SetFinancialYearOpenCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FinancialYear> found = await FindAsync(request.FinancialYearId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<FinancialYearSummary>(found.Error);
        }

        FinancialYear year = found.Value;
        Result changed = request.Open ? year.Reopen() : year.Close();

        if (changed.IsFailure)
        {
            return Result.Failure<FinancialYearSummary>(changed.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(year));
    }

    private static FinancialYearSummary Describe(FinancialYear year) =>
        new(year.Id.Value, year.Code, year.StartDate, year.EndDate, year.Status);

    private static Error NoFirm() =>
        Error.Forbidden("FinancialYear.NoFirmSelected", "A firm must be selected first.");

    private async Task<Result<FinancialYear>> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<FinancialYear>(NoFirm());
        }

        FinancialYear? year = await _years.FindAsync(FinancialYearId.From(id), cancellationToken);

        return year is null || year.FirmId != firmId
            ? Result.Failure<FinancialYear>(Error.NotFound(
                "FinancialYear.NotFound", "No such financial year in the selected firm."))
            : Result.Success(year);
    }
}
