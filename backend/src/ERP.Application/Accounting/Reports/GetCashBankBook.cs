using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>
/// Produces a cash book or a bank book: the movement on every cash or bank
/// account over a period, with a running balance.
/// </summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <param name="Kind">
/// <see cref="LedgerKind.Cash"/> for the cash book, <see cref="LedgerKind.Bank"/>
/// for the bank book. No other kind is accepted.
/// </param>
/// <param name="LedgerId">
/// Restricts the report to one account. Left null, every account of the kind
/// appears, which is what "the cash book" conventionally means when a firm keeps
/// more than one till.
/// </param>
/// <remarks>
/// Cash book and bank book are the same report over a different set of accounts,
/// so they are one query rather than two near-identical ones. They are exposed as
/// separate endpoints because that is how an accountant asks for them.
/// </remarks>
public sealed record GetCashBankBookQuery(
    DateOnly From,
    DateOnly To,
    LedgerKind Kind,
    Guid? LedgerId = null) : IQuery<CashBankBookResponse>;

/// <summary>One cash or bank account's movement over the period.</summary>
/// <param name="LedgerId">The account.</param>
/// <param name="LedgerCode">The account code.</param>
/// <param name="LedgerName">The account name.</param>
/// <param name="OpeningBalance">The balance brought forward, in debit-positive terms.</param>
/// <param name="ClosingBalance">The balance carried forward, in debit-positive terms.</param>
/// <param name="TotalReceipts">Money in: total debits to the account.</param>
/// <param name="TotalPayments">Money out: total credits to the account.</param>
/// <param name="Lines">The movements, oldest first, with a running balance.</param>
/// <remarks>
/// Receipts and payments rather than debits and credits. Cash and bank accounts
/// are assets, so a debit is money arriving and a credit is money leaving - and
/// that is how the people who read a cash book think about it.
/// </remarks>
public sealed record CashBankBookAccount(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal TotalReceipts,
    decimal TotalPayments,
    IReadOnlyList<LedgerStatementLine> Lines);

/// <summary>A cash book or bank book.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Kind">Which book this is.</param>
/// <param name="Currency">The base currency the figures are stated in.</param>
/// <param name="Accounts">One section per account, in code order.</param>
/// <param name="TotalOpeningBalance">Opening balance across every account.</param>
/// <param name="TotalClosingBalance">Closing balance across every account.</param>
/// <param name="TotalReceipts">Money in across every account.</param>
/// <param name="TotalPayments">Money out across every account.</param>
public sealed record CashBankBookResponse(
    DateOnly From,
    DateOnly To,
    LedgerKind Kind,
    string Currency,
    IReadOnlyList<CashBankBookAccount> Accounts,
    decimal TotalOpeningBalance,
    decimal TotalClosingBalance,
    decimal TotalReceipts,
    decimal TotalPayments);

/// <summary>Validates a <see cref="GetCashBankBookQuery"/>.</summary>
public sealed class GetCashBankBookQueryValidator : AbstractValidator<GetCashBankBookQuery>
{
    /// <summary>The longest period either book will return in one request.</summary>
    public const int MaximumRangeDays = 366;

    /// <summary>Initialises a new instance of the <see cref="GetCashBankBookQueryValidator"/> class.</summary>
    public GetCashBankBookQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");

        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaximumRangeDays)
            .WithMessage($"A cash or bank book cannot span more than {MaximumRangeDays} days.")
            .When(q => q.To >= q.From);

        // Only cash and bank accounts have a "book" in this sense. Allowing any
        // other kind would produce a report titled "cash book" listing customers.
        RuleFor(q => q.Kind)
            .Must(kind => kind is LedgerKind.Cash or LedgerKind.Bank)
            .WithMessage("Only cash and bank accounts have a book.");
    }
}

/// <summary>Handles <see cref="GetCashBankBookQuery"/>.</summary>
/// <remarks>
/// Composed from <see cref="ILedgerStatementReader"/> rather than given its own
/// query. A cash book is a statement of account per cash ledger, and that reader
/// is already proven - it carries the balance brought forward, the running
/// balance, and the contra ledgers that make each movement legible. Reusing it
/// means one implementation of that arithmetic rather than two that must be kept
/// in step.
/// <para>
/// It does cost one query per account. A firm has a handful of tills and bank
/// accounts, not hundreds, so the trade favours reuse; if that ever stops being
/// true this is the place to add a purpose-built reader.
/// </para>
/// </remarks>
public sealed class GetCashBankBookQueryHandler
    : IQueryHandler<GetCashBankBookQuery, CashBankBookResponse>
{
    private readonly ILedgerStatementReader _reader;
    private readonly ILedgerRepository _ledgers;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetCashBankBookQueryHandler"/> class.</summary>
    /// <param name="reader">The statement reader.</param>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetCashBankBookQueryHandler(
        ILedgerStatementReader reader,
        ILedgerRepository ledgers,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _ledgers = ledgers;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<CashBankBookResponse>> Handle(
        GetCashBankBookQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<CashBankBookResponse>(firm.Error);
        }

        IReadOnlyList<(Ledger Ledger, AccountGroup Group)> all =
            await _ledgers.ListWithGroupAsync(firm.Value.Id, activeOnly: false, cancellationToken);

        List<Ledger> accounts = [.. all
            .Select(pair => pair.Ledger)
            .Where(ledger => ledger.Kind == request.Kind)
            .OrderBy(ledger => ledger.Code, StringComparer.Ordinal)];

        if (request.LedgerId is { } requestedId)
        {
            LedgerId ledgerId = LedgerId.From(requestedId);
            accounts = [.. accounts.Where(ledger => ledger.Id == ledgerId)];

            if (accounts.Count == 0)
            {
                return Result.Failure<CashBankBookResponse>(Error.NotFound(
                    "Ledger.NotFound",
                    $"No such {request.Kind.ToString().ToLowerInvariant()} account in the " +
                    $"selected firm."));
            }
        }

        List<CashBankBookAccount> sections = new(accounts.Count);

        decimal totalOpening = 0m;
        decimal totalClosing = 0m;
        decimal totalReceipts = 0m;
        decimal totalPayments = 0m;

        foreach (Ledger account in accounts)
        {
            LedgerStatementData? data = await _reader.ReadAsync(
                account.Id, firm.Value.Id, request.From, request.To, cancellationToken);

            if (data is null)
            {
                continue;
            }

            List<LedgerStatementLine> lines = new(data.Postings.Count);

            decimal running = data.OpeningBalance;
            decimal receipts = 0m;
            decimal payments = 0m;

            foreach (LedgerPosting posting in data.Postings)
            {
                decimal debit = posting.Side == EntrySide.Debit ? posting.BaseAmount : 0m;
                decimal credit = posting.Side == EntrySide.Credit ? posting.BaseAmount : 0m;

                running += debit - credit;
                receipts += debit;
                payments += credit;

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

            sections.Add(new CashBankBookAccount(
                account.Id.Value,
                account.Code,
                account.Name,
                data.OpeningBalance,
                running,
                receipts,
                payments,
                lines));

            totalOpening += data.OpeningBalance;
            totalClosing += running;
            totalReceipts += receipts;
            totalPayments += payments;
        }

        return Result.Success(new CashBankBookResponse(
            request.From,
            request.To,
            request.Kind,
            firm.Value.BaseCurrency.Code,
            sections,
            totalOpening,
            totalClosing,
            totalReceipts,
            totalPayments));
    }
}
