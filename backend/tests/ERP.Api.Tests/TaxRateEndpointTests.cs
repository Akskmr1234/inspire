using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Masters;
using ERP.Application.Inventory.Products;

namespace ERP.Api.Tests;

/// <summary>The tax rate master, and the rate a product carries from it.</summary>
[Collection(ApiCollection.Name)]
public sealed class TaxRateEndpointTests
{
    private const string Rates = "/api/v1/inventory/tax-rates";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="TaxRateEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public TaxRateEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_firm_starts_with_exempt_and_a_default_vat_rate()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        List<TaxSlabSummary> rates = (await client.GetFromJsonAsync<List<TaxSlabSummary>>(
            $"{Rates}?includeInactive=true"))!;

        rates.ShouldContain(rate => rate.Code == "EXEMPT" && rate.Percentage == 0m);
        rates.ShouldContain(rate => rate.Code == "VAT-5" && rate.Percentage == 5m);
    }

    [Fact]
    public async Task A_rate_is_added_made_default_and_carried_by_a_product()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        Guid previousDefault = (await ListAsync(client)).Single(rate => rate.IsDefault).Id;

        // No code given: one is made from the name.
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Rates, new { Name = $"Luxury {tag}", Percentage = 12.5m });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = await created.Content.ReadFromJsonAsync<Guid>();

        TaxSlabSummary rate = (await ListAsync(client)).Single(row => row.Id == id);
        rate.Code.ShouldBe($"LUXURY-{tag}");
        rate.IsDefault.ShouldBeFalse();

        // Made the default, it takes the place of the one before.
        (await client.PutAsJsonAsync($"{Rates}/{id}", new { Name = rate.Name, Percentage = 12.5m, IsDefault = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        List<TaxSlabSummary> after = await ListAsync(client);
        after.Single(row => row.IsDefault).Id.ShouldBe(id);

        // A product filed under it reports its percentage, in the list and in detail.
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        ProductDetail product = (await client.GetFromJsonAsync<ProductDetail>(
            $"/api/v1/inventory/products/{masters.ProductId}"))!;

        (await client.PutAsJsonAsync(
            $"/api/v1/inventory/products/{masters.ProductId}/classification",
            new { product.CategoryId, BrandId = (Guid?)null, TaxSlabId = id }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        ProductDetail taxed = (await client.GetFromJsonAsync<ProductDetail>(
            $"/api/v1/inventory/products/{masters.ProductId}"))!;
        taxed.TaxSlabId.ShouldBe(id);
        taxed.TaxPercentage.ShouldBe(12.5m);

        List<ProductSummary> listed = (await client.GetFromJsonAsync<List<ProductSummary>>(
            $"/api/v1/inventory/products?search={masters.ProductCode}"))!;
        listed.Single(row => row.Id == masters.ProductId).TaxPercentage.ShouldBe(12.5m);

        // Withdrawn, it is no longer the default; the old default is put back for the
        // tests that follow.
        (await client.PostAsJsonAsync($"{Rates}/{id}/active", new { IsActive = false }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        TaxSlabSummary original = (await ListAsync(client)).Single(row => row.Id == previousDefault);
        (await client.PutAsJsonAsync(
            $"{Rates}/{previousDefault}",
            new { original.Name, original.Percentage, original.NameArabic, IsDefault = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(client)).ShouldNotContain(row => row.Id == id);
    }

    [Fact]
    public async Task A_percentage_over_one_hundred_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Rates, new { Name = "Typo", Percentage = 500m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_percentage_another_rate_already_carries_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // The firm starts with 5%; a second 5% is refused by name.
        HttpResponseMessage twin = await client.PostAsJsonAsync(
            Rates, new { Name = $"VAT again {tag}", Percentage = 5m });
        twin.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await twin.Content.ReadAsStringAsync()).ShouldContain("TaxSlab.DuplicatePercentage");

        // A rate of its own may be corrected, keeping its percentage, but not moved onto
        // another's.
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Rates, new { Name = $"Reduced {tag}", Percentage = 2.25m });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = await created.Content.ReadFromJsonAsync<Guid>();

        (await client.PutAsJsonAsync($"{Rates}/{id}", new { Name = $"Reduced rate {tag}", Percentage = 2.25m }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"{Rates}/{id}", new { Name = $"Reduced rate {tag}", Percentage = 5m }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Withdrawn, it still holds the percentage: it is restored, not made again.
        (await client.PostAsJsonAsync($"{Rates}/{id}/active", new { IsActive = false }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync(Rates, new { Name = $"Reduced again {tag}", Percentage = 2.25m }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<List<TaxSlabSummary>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TaxSlabSummary>>(Rates))!;
}
