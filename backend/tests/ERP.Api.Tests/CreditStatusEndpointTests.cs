using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Accounting.Reports;

namespace ERP.Api.Tests;

/// <summary>Tests for the credit position of a party, end to end.</summary>
/// <remarks>
/// The reading half of "a credit limit warns rather than blocks". What these cover is
/// that the endpoint reports a position rather than enforcing one, and the distinction
/// that decides who appears on a management report: a party with no limit agreed is not
/// a party whose limit is nothing.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class CreditStatusEndpointTests
{
    private const string Ledgers = "/api/v1/accounting/ledgers";

    private readonly ApiFactory _factory;

    public CreditStatusEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_party_with_nothing_outstanding_owes_nothing_and_is_within_any_limit()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid customerId = await CustomerAsync(client);

        CreditStatus status = (await client.GetFromJsonAsync<CreditStatus>(
            $"{Ledgers}/{customerId}/credit-status"))!;

        status.LedgerId.ShouldBe(customerId);
        status.Outstanding.ShouldBe(0m);
        status.Overdue.ShouldBe(0m);
        status.Currency.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_party_with_no_limit_agreed_is_never_over_it()
    {
        // "No limit agreed" and "a limit of nothing" are different arrangements. Reading
        // the first as a breach would put every cash customer on a management report.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid customerId = await CustomerAsync(client);

        CreditStatus status = (await client.GetFromJsonAsync<CreditStatus>(
            $"{Ledgers}/{customerId}/credit-status"))!;

        status.CreditLimit.ShouldBeNull();
        status.Available.ShouldBeNull();
        status.IsOverLimit.ShouldBeFalse();
    }

    [Fact]
    public async Task A_ledger_from_outside_the_firm_is_not_found()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"{Ledgers}/{Guid.NewGuid()}/credit-status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_position_refuses_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync(
            $"{Ledgers}/{Guid.NewGuid()}/credit-status");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Finds a customer ledger the seeded chart already provides.</summary>
    private static async Task<Guid> CustomerAsync(HttpClient client)
    {
        IReadOnlyList<LedgerSummary> ledgers =
            (await client.GetFromJsonAsync<IReadOnlyList<LedgerSummary>>(Ledgers))!;

        return ledgers[0].LedgerId;
    }
}
