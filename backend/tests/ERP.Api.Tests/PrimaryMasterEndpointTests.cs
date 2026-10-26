using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Masters;

namespace ERP.Api.Tests;

/// <summary>The Primary category and brand: always there, one each, and kept as they are.</summary>
[Collection(ApiCollection.Name)]
public sealed class PrimaryMasterEndpointTests
{
    private const string Inventory = "/api/v1/inventory";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="PrimaryMasterEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public PrimaryMasterEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Every_firm_has_one_Primary_category_and_brand_which_cannot_be_changed_or_withdrawn()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        // Read twice: made the first time, and not made again.
        await client.GetFromJsonAsync<List<CategorySummary>>($"{Inventory}/categories");
        CategorySummary category = (await client.GetFromJsonAsync<List<CategorySummary>>(
            $"{Inventory}/categories?includeInactive=true"))!.Where(row => row.IsPrimary).ShouldHaveSingleItem();
        category.Name.ShouldBe("Primary", StringCompareShould.IgnoreCase);
        category.IsActive.ShouldBeTrue();

        await client.GetFromJsonAsync<List<BrandSummary>>($"{Inventory}/brands");
        BrandSummary brand = (await client.GetFromJsonAsync<List<BrandSummary>>(
            $"{Inventory}/brands?includeInactive=true"))!.Where(row => row.IsPrimary).ShouldHaveSingleItem();
        brand.Name.ShouldBe("Primary", StringCompareShould.IgnoreCase);

        // It is what every new record defaults to, so it stays exactly as it is.
        (await client.PostAsJsonAsync($"{Inventory}/Category/{category.Id}/active", new { IsActive = false }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.PutAsJsonAsync($"{Inventory}/Category/{category.Id}", new { Name = "Renamed" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync($"{Inventory}/Brand/{brand.Id}/active", new { IsActive = false }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.PutAsJsonAsync($"{Inventory}/Brand/{brand.Id}", new { Name = "Renamed" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // A product can be filed under both.
        Guid unit = await CreateAsync(client, "units", new { Code = $"PU{Guid.NewGuid():N}"[..10], Name = $"Primary unit {Guid.NewGuid():N}"[..24] });
        HttpResponseMessage product = await client.PostAsJsonAsync($"{Inventory}/products", new
        {
            Description = $"Filed under Primary {Guid.NewGuid():N}"[..32],
            CategoryId = category.Id,
            BrandId = brand.Id,
            StockUnitId = unit,
            ItemType = 1,
        });
        product.StatusCode.ShouldBe(HttpStatusCode.Created, await product.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/{resource}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}
