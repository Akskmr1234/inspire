using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Masters;
using ERP.Application.Purchase;
using ERP.Application.Sales;

namespace ERP.Api.Tests;

/// <summary>Codes made for masters entered without one, and a category's parent.</summary>
[Collection(ApiCollection.Name)]
public sealed class MasterCodeEndpointTests
{
    private const string Inventory = "/api/v1/inventory";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="MasterCodeEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public MasterCodeEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_brand_entered_without_a_code_gets_one_from_its_name()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await CreateAsync(client, "brands", new { Name = $"Galaxy {tag}" });
        await CreateAsync(client, "brands", new { Name = $"Galaxy {tag}" });
        await CreateAsync(client, "brands", new { Name = "سامسونج" });

        List<BrandSummary> brands = (await client.GetFromJsonAsync<List<BrandSummary>>(
            $"{Inventory}/brands?includeInactive=true"))!;

        brands.Where(brand => brand.Name == $"Galaxy {tag}").Select(brand => brand.Code)
            .ShouldBe([$"GALAXY-{tag}", $"GALAXY-{tag}-2"], ignoreOrder: true);

        // A name with no Latin letters gets the kind's prefix and a number.
        brands.ShouldContain(brand => brand.Name == "سامسونج" && brand.Code.StartsWith("BRAND-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Customers_and_suppliers_entered_without_a_code_are_numbered()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage customer = await client.PostAsJsonAsync(
            "/api/v1/sales/customers", new { Name = "Numbered customer" });
        customer.StatusCode.ShouldBe(HttpStatusCode.Created, await customer.Content.ReadAsStringAsync());
        (await customer.Content.ReadFromJsonAsync<CustomerResponse>())!.Code.ShouldStartWith("CUS-");

        HttpResponseMessage supplier = await client.PostAsJsonAsync(
            "/api/v1/purchase/suppliers", new { Name = "Numbered supplier" });
        supplier.StatusCode.ShouldBe(HttpStatusCode.Created, await supplier.Content.ReadAsStringAsync());
        (await supplier.Content.ReadFromJsonAsync<SupplierResponse>())!.Code.ShouldStartWith("SUP-");
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

    private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/{resource}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}
