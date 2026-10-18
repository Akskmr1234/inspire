using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Accounting.Reports;
using ERP.Application.Accounting.Vouchers;
using ERP.Application.Platform.Dashboards;
using ERP.Domain.Accounting;

namespace ERP.Api.Tests;

/// <summary>
/// What the payment screen sends - several ledgers paid from one account, by cheque - and
/// the trading figures the dashboard draws from the postings.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MoneyEntryEndpointTests
{
    private const string Ledgers = "/api/v1/accounting/ledgers";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="MoneyEntryEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public MoneyEntryEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task One_cheque_paying_two_ledgers_is_one_cheque_in_the_register_with_its_payee()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid cleaning = await ExpenseLedgerAsync(client, $"Cleaning {tag}");
        Guid repairs = await ExpenseLedgerAsync(client, $"Repairs {tag}");
        Guid bank = (await LedgersAsync(client)).First(ledger => ledger.Kind == LedgerKind.Bank).LedgerId;

        object Cheque(decimal amount) => new
        {
            ChequeNumber = $"Q{tag}",
            InstrumentDate = Today,
            Amount = amount,
            BankLedgerId = bank,
            AccountNumber = "0012345678",
            InFavourOf = $"Al Rayyan Services {tag}",
        };

        HttpResponseMessage paid = await client.PostAsJsonAsync("/api/v1/accounting/vouchers", new
        {
            Type = VoucherType.BankPayment,
            Date = Today,
            PaymentMode = "Cheque",
            PostImmediately = true,
            Lines = new object[]
            {
                new { LedgerId = cleaning, Side = EntrySide.Debit, Amount = 120m, Cheques = new[] { Cheque(120m) } },
                new { LedgerId = repairs, Side = EntrySide.Debit, Amount = 80m, Cheques = new[] { Cheque(80m) } },
                new { LedgerId = bank, Side = EntrySide.Credit, Amount = 200m },
            },
        });

        paid.StatusCode.ShouldBe(HttpStatusCode.Created, await paid.Content.ReadAsStringAsync());

        ChequeRegisterResponse register = (await client.GetFromJsonAsync<ChequeRegisterResponse>(
            $"/api/v1/accounting/reports/cheque-register?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))!;

        // One leaf of the chequebook, for the whole payment, with who it was made out to.
        ChequeReportLine cheque = register.Cheques.Single(row => row.ChequeNumber == $"Q{tag}");
        cheque.Amount.ShouldBe(200m);
        cheque.InFavourOf.ShouldBe($"Al Rayyan Services {tag}");
        cheque.AccountNumber.ShouldBe("0012345678");

        // The same leaf cannot be written again from the same account while it is open.
        HttpResponseMessage again = await client.PostAsJsonAsync("/api/v1/accounting/vouchers", new
        {
            Type = VoucherType.BankPayment,
            Date = Today,
            PaymentMode = "Cheque",
            PostImmediately = true,
            Lines = new object[]
            {
                new { LedgerId = repairs, Side = EntrySide.Debit, Amount = 10m, Cheques = new[] { Cheque(10m) } },
                new { LedgerId = bank, Side = EntrySide.Credit, Amount = 10m },
            },
        });

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_expense_paid_today_shows_in_the_days_expenses_and_profit()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid cleaning = await ExpenseLedgerAsync(client, $"Sweeping {tag}");
        Guid cash = (await LedgersAsync(client)).Single(ledger => ledger.Code == "CASH").LedgerId;

        IReadOnlyDictionary<string, decimal> before = await TradingAsync(client);

        HttpResponseMessage paid = await client.PostAsJsonAsync("/api/v1/accounting/vouchers", new
        {
            Type = VoucherType.CashPayment,
            Date = Today,
            PaymentMode = "Cash",
            PostImmediately = true,
            Lines = new object[]
            {
                new { LedgerId = cleaning, Side = EntrySide.Debit, Amount = 45m },
                new { LedgerId = cash, Side = EntrySide.Credit, Amount = 45m },
            },
        });
        paid.StatusCode.ShouldBe(HttpStatusCode.Created, await paid.Content.ReadAsStringAsync());

        IReadOnlyDictionary<string, decimal> after = await TradingAsync(client);

        (after[DashboardMetrics.ExpensesToday] - before[DashboardMetrics.ExpensesToday]).ShouldBe(45m);
        (after[DashboardMetrics.ExpensesThisMonth] - before[DashboardMetrics.ExpensesThisMonth]).ShouldBe(45m);
        (after[DashboardMetrics.ProfitToday] - before[DashboardMetrics.ProfitToday]).ShouldBe(-45m);
        (after[DashboardMetrics.SalesToday] - before[DashboardMetrics.SalesToday]).ShouldBe(0m);
    }

    private static async Task<IReadOnlyDictionary<string, decimal>> TradingAsync(HttpClient client)
    {
        DashboardsResponse dashboards = (await client.GetFromJsonAsync<DashboardsResponse>("/api/v1/dashboards"))!;
        DashboardView overview = dashboards.Dashboards.Single(dashboard => dashboard.Code == "accounting-overview");

        overview.Widgets.Select(widget => widget.MetricCode).ShouldContain(DashboardMetrics.ProfitToday);

        DashboardDataResponse data = (await client.GetFromJsonAsync<DashboardDataResponse>(
            $"/api/v1/dashboards/{overview.Id}/data"))!;

        return data.Metrics
            .Where(metric => metric.MetricCode is not null)
            .GroupBy(metric => metric.MetricCode!)
            .ToDictionary(group => group.Key, group => group.First().Value);
    }

    private static async Task<List<LedgerSummary>> LedgersAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<LedgerSummary>>(Ledgers))!;

    private static async Task<Guid> ExpenseLedgerAsync(HttpClient client, string name)
    {
        List<AccountGroupSummary> groups = (await client.GetFromJsonAsync<List<AccountGroupSummary>>(
            "/api/v1/accounting/account-groups"))!;

        HttpResponseMessage created = await client.PostAsJsonAsync(Ledgers, new
        {
            Name = name,
            AccountGroupId = groups.Single(group => group.Code == "5200").AccountGroupId,
            Kind = LedgerKind.General,
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<LedgerDetail>())!.LedgerId;
    }
}
