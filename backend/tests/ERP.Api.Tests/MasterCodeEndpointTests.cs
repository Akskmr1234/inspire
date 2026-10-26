using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Masters;
using ERP.Application.Purchase;
using ERP.Application.Sales;

namespace ERP.Api.Tests;

/// <summary>Codes issued for masters entered without one, and a category's parent.</summary>
[Collection(ApiCollection.Name)]
public sealed class MasterCodeEndpointTests
{
    private const string Inventory = "/api/v1/inventory";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="MasterCodeEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public MasterCodeEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_brand_entered_without_a_code_is_numbered_from_10000_past_any_typed_code()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid first = await CreateAsync(client, "brands", new { Name = $"Galaxy {tag}" });
        long issued = long.Parse(await BrandCodeAsync(client, first), CultureInfo.InvariantCulture);
        issued.ShouldBeGreaterThanOrEqualTo(10000);

        // A typed code is kept as typed, and the sequence does not move for it - even when
        // the code typed is the number the sequence would have issued next.
        string typed = (issued + 1).ToString(CultureInfo.InvariantCulture);
        Guid manual = await CreateAsync(client, "brands", new { Name = $"Typed {tag}", Code = typed });
        (await BrandCodeAsync(client, manual)).ShouldBe(typed);

        // So the next issued code steps over it rather than clashing with it.
        Guid next = await CreateAsync(client, "brands", new { Name = $"Galaxy next {tag}" });
        (await BrandCodeAsync(client, next)).ShouldBe((issued + 2).ToString(CultureInfo.InvariantCulture));

        // A typed code another brand holds is refused, as is a name it holds in any case.
        (await client.PostAsJsonAsync($"{Inventory}/brands", new { Name = $"Other {tag}", Code = typed }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync($"{Inventory}/brands", new { Name = $"galaxy {tag}" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A name with no Latin letters is numbered like any other.
        Guid arabic = await CreateAsync(client, "brands", new { Name = $"سامسونج {tag}" });
        (await BrandCodeAsync(client, arabic)).ShouldBe((issued + 3).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Customers_and_suppliers_entered_without_a_code_are_numbered()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6];

        HttpResponseMessage customer = await client.PostAsJsonAsync(
            "/api/v1/sales/customers", new { Name = $"Numbered customer {tag}" });
        customer.StatusCode.ShouldBe(HttpStatusCode.Created, await customer.Content.ReadAsStringAsync());
        string customerCode = (await customer.Content.ReadFromJsonAsync<CustomerResponse>())!.Code;
        long.Parse(customerCode, CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(10000);

        HttpResponseMessage supplier = await client.PostAsJsonAsync(
            "/api/v1/purchase/suppliers", new { Name = $"Numbered supplier {tag}" });
        supplier.StatusCode.ShouldBe(HttpStatusCode.Created, await supplier.Content.ReadAsStringAsync());
        string supplierCode = (await supplier.Content.ReadFromJsonAsync<SupplierResponse>())!.Code;
        long.Parse(supplierCode, CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(10000);

        // Customers and suppliers are both ledgers, so they never share a code.
        supplierCode.ShouldNotBe(customerCode);
    }

    [Fact]
    public async Task A_category_moves_under_another_but_never_under_its_own_child()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid phones = await CreateAsync(client, "categories", new { Name = $"Phones {tag}" });
        Guid smart = await CreateAsync(client, "categories", new { Name = $"Smart {tag}", ParentId = phones });
        Guid watches = await CreateAsync(client, "categories", new { Name = $"Watches {tag}" });

        // Watches moves under Phones.
        (await client.PutAsJsonAsync($"{Inventory}/Category/{watches}", new { Name = $"Watches {tag}", ParentId = phones }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        List<CategorySummary> categories = (await client.GetFromJsonAsync<List<CategorySummary>>(
            $"{Inventory}/categories?includeInactive=true"))!;
        categories.Single(category => category.Id == watches).ParentId.ShouldBe(phones);

        // Phones cannot go beneath its own child.
        HttpResponseMessage circular = await client.PutAsJsonAsync(
            $"{Inventory}/Category/{phones}", new { Name = $"Phones {tag}", ParentId = smart });
        circular.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await circular.Content.ReadAsStringAsync());

        // And back to the top level.
        (await client.PutAsJsonAsync($"{Inventory}/Category/{watches}", new { Name = $"Watches {tag}", ParentId = (Guid?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<CategorySummary>>($"{Inventory}/categories?includeInactive=true"))!
            .Single(category => category.Id == watches).ParentId.ShouldBeNull();
    }

    private static async Task<string> BrandCodeAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<BrandSummary>>($"{Inventory}/brands?includeInactive=true"))!
            .Single(brand => brand.Id == id).Code;

    private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/{resource}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}
