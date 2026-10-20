using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Sales;

/// <summary>Where a customer stands, for the counter selling to them.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="AsAt">The date to state it as at. Today when omitted.</param>
public sealed record GetCustomerAccountQuery(Guid CustomerId, DateOnly? AsAt = null)
    : IQuery<CustomerAccount>;

/// <summary>A customer's account, as the sales screen shows it beside a new sale.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Currency">The currency the figures are stated in.</param>
/// <param name="Balance">
/// What they owe across everything posted to them, opening balance included. Negative
/// where they have paid in advance.
/// </param>
/// <param name="Outstanding">What their unpaid invoices come to.</param>
/// <param name="CreditLimit">What they may owe, where a limit was agreed.</param>
/// <param name="CreditDays">How long they have to pay, where a period was agreed.</param>
/// <param name="Available">
/// What is left of the limit; negative once it is passed, and absent with no limit.
/// </param>
/// <param name="IsOverLimit">Whether their invoices are already past the limit.</param>
public sealed record CustomerAccount(
    Guid CustomerId,
    string Currency,
    decimal Balance,
    decimal Outstanding,
    decimal? CreditLimit,
    int? CreditDays,
    decimal? Available,
    bool IsOverLimit);

/// <summary>Handles <see cref="GetCustomerAccountQuery"/>.</summary>
/// <remarks>
/// Two figures, because a counter needs both and they differ. The balance is what the
/// customer owes overall and is what "previous balance" on a bill means; the outstanding
/// is what their unpaid invoices come to, which is what a credit limit is measured
/// against. A customer who paid on account without naming an invoice owes less than their
/// invoices say, and the two figures are how the screen can show it.
/// </remarks>
public sealed class GetCustomerAccountQueryHandler
    : IQueryHandler<GetCustomerAccountQuery, CustomerAccount>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ILedgerBalanceReader _balances;
    private readonly IOutstandingBillsReader _bills;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="GetCustomerAccountQueryHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="balances">The ledger balance reader.</param>
    /// <param name="bills">The outstanding bills reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    public GetCustomerAccountQueryHandler(
        ILedgerRepository ledgers,
        ILedgerBalanceReader balances,
        IOutstandingBillsReader bills,
        ITenantContext tenantContext,
        IClock clock)
    {
        _ledgers = ledgers;
        _balances = balances;
        _bills = bills;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerAccount>> Handle(
        GetCustomerAccountQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<CustomerAccount>(Error.Forbidden(
                "Customer.NoFirmSelected", "A firm must be selected to read a customer."));
        }

        Ledger? customer = await _ledgers.FindAsync(
            LedgerId.From(request.CustomerId), cancellationToken);

        if (customer is null || customer.FirmId != firmId || customer.Kind != LedgerKind.Customer)
        {
            return Result.Failure<CustomerAccount>(Error.NotFound(
                "Customer.NotFound", "No such customer in the selected firm."));
        }

        DateOnly asAt = request.AsAt ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        decimal balance = await _balances.GetBalanceAsync(
            firmId, customer.Id, asAt, cancellationToken);

        IReadOnlyList<OutstandingBillRow> open = await _bills.ReadAsync(
            firmId, BillType.Receivable, asAt, customer.Id, cancellationToken);

        decimal outstanding = open.Sum(bill => bill.OutstandingAmount);
        decimal? available = customer.CreditLimit is { } limit ? limit - outstanding : null;

        return Result.Success(new CustomerAccount(
            customer.Id.Value,
            customer.Currency.Code,
            balance,
            outstanding,
            customer.CreditLimit,
            customer.CreditDays,
            available,
            available is < 0m));
    }
}
