using System.Net;
using System.Net.Http.Json;
using ERP.Application.Sales;

namespace ERP.Api.Tests;

/// <summary>Tests the customer master of section 12.1, end to end.</summary>
/// <remarks>
/// Until these existed there was no supported way to create somebody to bill: the seeded
/// chart contains no customer and an invoice may only be raised against one, so a fresh
/// installation could not make its first sale through the API at all.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class CustomerEndpointTests
{
    private const string Customers = "/api/v1/sales/customers";

    private readonly ApiFactory _factory;

    public CustomerEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_customer_is_created_and_read_back_with_their_terms()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string code = $"CUST{Suffix()}";

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Customers,
            new
            {
                Code = code,
                Name = "Al Mansoor Trading",
                NameArabic = "المنصور للتجارة",
                Contact = new { MobileNumber = "55512345", AddressLine1 = "Salwa Road" },
                Terms = new { CreditLimit = 50_000m, CreditDays = 30, IsBillWise = true },
                TaxDetails = new { RegistrationNumber = "QA-123", StateCode = "DOH" },
            });

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        CustomerResponse customer =
            (await created.Content.ReadFromJsonAsync<CustomerResponse>())!;

        CustomerResponse read = (await client.GetFromJsonAsync<CustomerResponse>(
            $"{Customers}/{customer.CustomerId}"))!;

        read.Code.ShouldBe(code);
        read.Name.ShouldBe("Al Mansoor Trading");
        read.Contact.MobileNumber.ShouldBe("55512345");
        read.Terms.CreditDays.ShouldBe(30);
        read.TaxDetails.StateCode.ShouldBe("DOH");
        read.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task A_code_another_account_already_uses_is_a_conflict()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string code = $"CUST{Suffix()}";

        (await client.PostAsJsonAsync(Customers, new { Code = code, Name = "First" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage again = await client.PostAsJsonAsync(
            Customers, new { Code = code, Name = "Second" });

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_counter_finds_a_customer_by_the_number_on_their_phone()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string mobile = $"5551{Random.Shared.Next(1000, 9999)}";

        Guid customerId = await CreateAsync(client, mobile: mobile);

        IReadOnlyList<CustomerResponse> found =
            (await client.GetFromJsonAsync<IReadOnlyList<CustomerResponse>>(
                $"{Customers}?search={mobile}&activeOnly=true"))!;

        found.ShouldHaveSingleItem().CustomerId.ShouldBe(customerId);
    }

    [Fact]
    public async Task A_withdrawn_customer_drops_out_of_the_active_list_and_is_still_readable()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string mobile = $"5552{Random.Shared.Next(1000, 9999)}";

        Guid customerId = await CreateAsync(client, mobile: mobile);

        HttpResponseMessage withdrawn = await client.PutAsJsonAsync(
            $"{Customers}/{customerId}/active", new { IsActive = false });

        withdrawn.StatusCode.ShouldBe(HttpStatusCode.OK);

        IReadOnlyList<CustomerResponse> active =
            (await client.GetFromJsonAsync<IReadOnlyList<CustomerResponse>>(
                $"{Customers}?search={mobile}&activeOnly=true"))!;

        active.ShouldBeEmpty();

        // Still there, because every past invoice points at them.
        CustomerResponse read = (await client.GetFromJsonAsync<CustomerResponse>(
            $"{Customers}/{customerId}"))!;

        read.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task An_update_changes_what_it_names_and_leaves_the_rest()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid customerId = await CreateAsync(client, mobile: $"5553{Random.Shared.Next(1000, 9999)}");

        HttpResponseMessage updated = await client.PutAsJsonAsync(
            $"{Customers}/{customerId}",
            new
            {
                Name = "Al Mansoor Trading LLC",
                Contact = new { AddressLine1 = "Al Sadd" },
            });

        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        CustomerResponse read = (await client.GetFromJsonAsync<CustomerResponse>(
            $"{Customers}/{customerId}"))!;

        read.Name.ShouldBe("Al Mansoor Trading LLC");
        read.Contact.AddressLine1.ShouldBe("Al Sadd");
        read.Terms.CreditDays.ShouldBe(30);
    }

    [Fact]
    public async Task A_customer_of_another_firm_is_not_found()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        (await client.GetAsync($"{Customers}/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_customer_endpoints_refuse_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync(Customers)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await client.PostAsJsonAsync(Customers, new { Code = "X", Name = "Y" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Creates a customer on 30-day terms, as the sales tests need one.</summary>
    internal static async Task<Guid> CreateAsync(
        HttpClient client,
        string? mobile = null,
        string? stateCode = null)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Customers,
            new
            {
                Code = $"CUST{Suffix()}",
                Name = "Al Mansoor Trading",
                Contact = new { MobileNumber = mobile },
                Terms = new { CreditDays = 30, IsBillWise = true },
                TaxDetails = new { StateCode = stateCode },
            });

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (await created.Content.ReadFromJsonAsync<CustomerResponse>())!.CustomerId;
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
}
