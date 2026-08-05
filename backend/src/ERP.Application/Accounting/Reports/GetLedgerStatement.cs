using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>Produces a statement of account for one ledger over a date range.</summary>
/// <param name="LedgerId">The ledger to report on.</param>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
public sealed record GetLedgerStatementQuery(Guid LedgerId, DateOnly From, DateOnly To)
    : IQuery<LedgerStatementResponse>;

/// <summary>One posting on a statement of account.</summary>
/// <param name="Date">The document date.</param>
/// <param name="VoucherId">The voucher, so the client can drill through to it.</param>
/// <param name="VoucherNumber">The document number.</param>
/// <param name="VoucherType">The kind of voucher.</param>
/// <param name="ReferenceNumber">The related reference, if any.</param>
/// <param name="Narration">The line narration, falling back to the voucher's.</param>
/// <param name="ContraLedgerNames">
/// The ledgers on the opposite side of this entry.
/// </param>
/// <param name="Debit">The amount when this posting debits the ledger.</param>
/// <param name="Credit">The amount when this posting credits the ledger.</param>
/// <param name="RunningBalance">
/// The balance after this posting, in debit-positive terms.
/// </param>
/// <remarks>
/// <see cref="ContraLedgerNames"/> is what makes the statement readable. A line
/// saying only "Cash 500.00 Dr" tells an accountant nothing; "Cash 500.00 Dr,
/// contra: Sales Account" tells them what the money was for. The reference
/// application shows this as the "particulars" column.
/// </remarks>
public sealed record LedgerStatementLine(
    DateOnly Date,
    Guid VoucherId,
    string VoucherNumber,
    VoucherType VoucherType,
    string? ReferenceNumber,
    string? Narration,
    IReadOnlyList<string> ContraLedgerNames,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

/// <summary>A statement of account.</summary>
/// <param name="LedgerId">The ledger reported on.</param>
/// <param name="LedgerCode">The ledger code.</param>
/// <param name="LedgerName">The ledger name.</param>
/// <param name="GroupName">The account group it reports under.</param>
/// <param name="Currency">The base currency the figures are stated in.</param>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="OpeningBalance">The balance brought forward, in debit-positive terms.</param>
/// <param name="ClosingBalance">The balance carried forward, in debit-positive terms.</param>
/// <param name="TotalDebit">Total debits in the period.</param>
/// <param name="TotalCredit">Total credits in the period.</param>
/// <param name="Lines">The postings, oldest first.</param>
public sealed record LedgerStatementResponse(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    string GroupName,
    string Currency,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<LedgerStatementLine> Lines);

/// <summary>Validates a <see cref="GetLedgerStatementQuery"/>.</summary>
public sealed class GetLedgerStatementQueryValidator : AbstractValidator<GetLedgerStatementQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetLedgerStatementQueryValidator"/> class.</summary>
    public GetLedgerStatementQueryValidator()
    {
        RuleFor(q => q.LedgerId).NotEqual(Guid.Empty);
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");
    }
}

/// <summary>Reads the postings behind a statement of account.</summary>
public interface ILedgerStatementReader
{
    /// <summary>
    /// Reads a ledger's postings within a period, plus the balance brought forward.
    /// </summary>
    /// <param name="ledgerId">The ledger.</param>
    /// <param name="firmId">The firm, so a ledger from another firm cannot be read.</param>
    /// <param name="from">The first date of the period.</param>
    /// <param name="to">The last date of the period.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledger's details and postings, or <see langword="null"/> if it does not exist.</returns>
    Task<LedgerStatementData?> ReadAsync(
        LedgerId ledgerId,
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw statement data, before the running balance is accumulated.</summary>
/// <param name="LedgerCode">The ledger code.</param>
/// <param name="LedgerName">The ledger name.</param>
/// <param name="GroupName">The account group name.</param>
/// <param name="OpeningBalance">The balance brought forward, in debit-positive terms.</param>
/// <param name="Postings">The postings in the period, oldest first.</param>
public sealed record LedgerStatementData(
    string LedgerCode,
    string LedgerName,
    string GroupName,
    decimal OpeningBalance,
    IReadOnlyList<LedgerPosting> Postings);

/// <summary>One posting against a ledger, before presentation.</summary>
/// <param name="Date">The document date.</param>
/// <param name="VoucherId">The voucher.</param>
/// <param name="VoucherNumber">The document number.</param>
/// <param name="VoucherType">The kind of voucher.</param>
/// <param name="ReferenceNumber">The related reference.</param>
/// <param name="Narration">The line narration, or the voucher's if the line has none.</param>
/// <param name="ContraLedgerNames">The ledgers on the opposite side.</param>
/// <param name="Side">Whether this posting debits or credits the ledger.</param>
/// <param name="BaseAmount">The amount in the firm's base currency.</param>
public sealed record LedgerPosting(
    DateOnly Date,
    Guid VoucherId,
    string VoucherNumber,
    VoucherType VoucherType,
    string? ReferenceNumber,
    string? Narration,
    IReadOnlyList<string> ContraLedgerNames,
    EntrySide Side,
    decimal BaseAmount);

/// <summary>Handles <see cref="GetLedgerStatementQuery"/>.</summary>
public sealed class GetLedgerStatementQueryHandler
    : IQueryHandler<GetLedgerStatementQuery, LedgerStatementResponse>
{
    private readonly ILedgerStatementReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetLedgerStatementQueryHandler"/> class.</summary>
    /// <param name="reader">The statement reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetLedgerStatementQueryHandler(
        ILedgerStatementReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<LedgerStatementResponse>> Handle(
        GetLedgerStatementQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<LedgerStatementResponse>(firm.Error);
        }

        LedgerStatementData? data = await _reader.ReadAsync(
            LedgerId.From(request.LedgerId), firm.Value.Id,
            request.From, request.To, cancellationToken);

        if (data is null)
        {
            return Result.Failure<LedgerStatementResponse>(Error.NotFound(
                "Ledger.NotFound", "No such ledger in the selected firm."));
        }

        // The running balance is accumulated here rather than in SQL. A window
        // function could do it, but the arithmetic is the part a reader checks by
        // hand against the printed statement, and having it in one readable loop is
        // worth more than saving a pass over a few hundred rows.
        List<LedgerStatementLine> lines = new(data.Postings.Count);

        decimal running = data.OpeningBalance;
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach (LedgerPosting posting in data.Postings)
        {
            decimal debit = posting.Side == EntrySide.Debit ? posting.BaseAmount : 0m;
            decimal credit = posting.Side == EntrySide.Credit ? posting.BaseAmount : 0m;

            running += debit - credit;
            totalDebit += debit;
            totalCredit += credit;

            lines.Add(new LedgerStatementLine(
                posting.Date,
                posting.VoucherId,
                posting.VoucherNumber,
                posting.VoucherType,
                posting.ReferenceNumber,
                posting.Narration,
                posting.ContraLedgerNames,
                debit,
                credit,
                running));
        }

        return Result.Success(new LedgerStatementResponse(
            request.LedgerId,
            data.LedgerCode,
            data.LedgerName,
            data.GroupName,
            firm.Value.BaseCurrency.Code,
            request.From,
            request.To,
            data.OpeningBalance,
            running,
            totalDebit,
            totalCredit,
            lines));
    }
}
