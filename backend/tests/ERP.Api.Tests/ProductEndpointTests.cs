using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Products;

namespace ERP.Api.Tests;

/// <summary>Tests for the product master, end to end through the real host.</summary>
/// <remarks>
/// The product master is the record every other module reaches for, and most of it is
/// fields on a form. What is tested here is the part that is not: the issued code that
/// must not be reissued, the search that has to reach a barcode, the units that must
/// convert, and the two flags that mean different things and are routinely confused.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ProductEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Products = $"{Inventory}/products";

    private readonly ApiFactory _factory;

    public ProductEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_product_is_created_read_back_and_listed()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(
            client, fixtures, "Blue widget", code: $"WID{fixtures.Suffix}");

        ProductDetail product = await ReadAsync(client, id);
        product.Code.ShouldBe($"WID{fixtures.Suffix}");
        product.Description.ShouldBe("Blue widget");
        product.CategoryId.ShouldBe(fixtures.CategoryId);
        product.StockUnitId.ShouldBe(fixtures.EachId);
        product.IsActive.ShouldBeTrue();

        // Both trading units default to the stock unit rather than to nothing, so a
        // product created from the minimum fields is immediately usable on a document.
        product.PurchaseUnitId.ShouldBe(fixtures.EachId);
        product.SalesUnitId.ShouldBe(fixtures.EachId);

        // The firm's currency, not a per-product one.
        product.Currency.ShouldNotBeNullOrWhiteSpace();

        IReadOnlyList<ProductSummary> listed = await ListAsync(client, fixtures.Suffix);
        ProductSummary summary = listed.Single(row => row.Id == id);
        summary.CategoryName.ShouldBe($"Widgets {fixtures.Suffix}");
        summary.StockUnitCode.ShouldBe($"EA{fixtures.Suffix}");
    }

    [Fact]
    public async Task An_omitted_code_is_issued_from_the_firms_own_sequence()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid firstId = await CreateProductAsync(client, fixtures, "Issued one");
        Guid secondId = await CreateProductAsync(client, fixtures, "Issued two");

        string first = (await ReadAsync(client, firstId)).Code;
        string second = (await ReadAsync(client, secondId)).Code;

        first.ShouldStartWith("PRO-");
        second.ShouldStartWith("PRO-");

        // Parsed from the highest issued number rather than counted. A count would
        // reissue a code the moment anything was withdrawn - and products are withdrawn
        // rather than deleted.
        int.Parse(second[4..], System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeGreaterThan(
                int.Parse(first[4..], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_duplicate_code_is_refused_with_a_conflict()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await CreateProductAsync(client, fixtures, "First", code: $"DUP{fixtures.Suffix}");

        HttpResponseMessage response = await PostProductAsync(
            client, fixtures, "Second", code: $"DUP{fixtures.Suffix}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_category_from_another_firm_is_not_found()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Products,
            new
            {
                Description = "Orphan",
                CategoryId = Guid.NewGuid(),
                StockUnitId = fixtures.EachId,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_product_is_searched_by_code_description_and_barcode()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(
            client, fixtures, $"Searchable lamp {fixtures.Suffix}",
            code: $"LMP{fixtures.Suffix}");

        string barcode = $"55{fixtures.Suffix}00";
        await AddBarcodeAsync(client, id, barcode);

        (await ListAsync(client, $"LMP{fixtures.Suffix}"))
            .ShouldContain(row => row.Id == id);
        (await ListAsync(client, $"Searchable lamp {fixtures.Suffix}"))
            .ShouldContain(row => row.Id == id);

        // The barcode reach is the point: on a counter the label is scanned, and the
        // number on it is frequently not the code in the master.
        (await ListAsync(client, barcode)).ShouldContain(row => row.Id == id);
    }

    [Fact]
    public async Task A_barcode_carries_its_own_rates_or_the_products()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(client, fixtures, "Multi-pack");

        string plain = $"10{fixtures.Suffix}";
        string priced = $"20{fixtures.Suffix}";

        await AddBarcodeAsync(client, id, plain);
        Guid pricedId = await AddBarcodeAsync(
            client, id, priced, cost: 40m, retailRate: 55m);

        ProductDetail product = await ReadAsync(client, id);
        product.Barcodes.Count.ShouldBe(2);

        product.Barcodes.Single(row => row.Barcode == plain).RetailRate.ShouldBe(0m);
        product.Barcodes.Single(row => row.Barcode == priced).RetailRate.ShouldBe(55m);

        // A duplicate on the same product is refused: one scan must resolve to one price.
        HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            $"{Products}/{id}/barcodes", new { Barcode = priced });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        HttpResponseMessage removed = await client.DeleteAsync(
            $"{Products}/{id}/barcodes/{pricedId}");
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReadAsync(client, id)).Barcodes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_retail_rate_above_the_printed_maximum_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(client, fixtures, "Priced goods");

        // Below cost is accepted - a loss-leader is a decision.
        HttpResponseMessage lossLeader = await SetRatesAsync(
            client, id, cost: 100m, retailRate: 80m);
        lossLeader.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Above the printed price is not: an MRP is a legal ceiling.
        HttpResponseMessage overMrp = await SetRatesAsync(
            client, id, cost: 100m, retailRate: 150m, maximumRetailPrice: 120m);
        overMrp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await ReadAsync(client, id)).RetailRate.ShouldBe(80m);
    }

    [Fact]
    public async Task A_trading_unit_must_convert_to_the_stock_unit()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(client, fixtures, "Bought by the box");

        // A box of the stock unit converts, so buying in boxes is allowed.
        HttpResponseMessage converts = await SetStockingAsync(
            client, id, fixtures.BoxId, fixtures.EachId, reorderLevel: 24m);
        converts.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        ProductDetail product = await ReadAsync(client, id);
        product.PurchaseUnitId.ShouldBe(fixtures.BoxId);
        product.ReorderLevel.ShouldBe(24m);

        // A unit from another group does not, and a stock figure derived from it would
        // mean nothing.
        HttpResponseMessage refused = await SetStockingAsync(
            client, id, fixtures.KilogramId, fixtures.EachId);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Batches_and_serial_numbers_are_independent()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(client, fixtures, "Handset");

        // A handset arrives in a batch and still carries its own IMEI. Refusing the
        // pair would misdescribe how phones are actually stocked.
        HttpResponseMessage both = await SetStockingAsync(
            client, id, fixtures.EachId, fixtures.EachId,
            tracksBatches: true, tracksSerialNumbers: true, shelfLifeDays: 365);
        both.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        ProductDetail product = await ReadAsync(client, id);
        product.TracksBatches.ShouldBeTrue();
        product.TracksSerialNumbers.ShouldBeTrue();
        product.ShelfLifeDays.ShouldBe(365);
    }

    [Fact]
    public async Task Discontinuing_a_product_is_not_the_same_as_withdrawing_it()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(
            client, fixtures, "Last of the line", code: $"END{fixtures.Suffix}");

        HttpResponseMessage discontinued = await client.PostAsJsonAsync(
            $"{Products}/{id}/discontinued", new { Value = true });
        discontinued.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Still listed. Stock on hand is still sold down; hiding it would strand it.
        IReadOnlyList<ProductSummary> listed = await ListAsync(client, fixtures.Suffix);
        listed.Single(row => row.Id == id).IsDiscontinued.ShouldBeTrue();

        HttpResponseMessage withdrawn = await client.PostAsJsonAsync(
            $"{Products}/{id}/active", new { Value = false });
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Gone from the default list, and still there when asked for.
        (await ListAsync(client, fixtures.Suffix)).ShouldNotContain(row => row.Id == id);
        (await ListAsync(client, fixtures.Suffix, includeInactive: true))
            .ShouldContain(row => row.Id == id);
    }

    [Fact]
    public async Task A_products_description_is_editable_and_its_code_is_not()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(
            client, fixtures, "Typo widgit", code: $"FIX{fixtures.Suffix}");

        HttpResponseMessage described = await client.PutAsJsonAsync(
            $"{Products}/{id}/description",
            new
            {
                Description = "Typo widget",
                DescriptionArabic = "أداة",
                Manufacturer = "Acme",
                Rack = "R1",
                Bin = "B2",
            });
        described.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        ProductDetail product = await ReadAsync(client, id);
        product.Description.ShouldBe("Typo widget");
        product.DescriptionArabic.ShouldBe("أداة");
        product.Manufacturer.ShouldBe("Acme");
        product.Rack.ShouldBe("R1");

        // The code is how the product is named on every document already entered, so
        // nothing exposes a way to change it.
        product.Code.ShouldBe($"FIX{fixtures.Suffix}");
    }

    [Fact]
    public async Task Device_attributes_are_recorded_against_any_product()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid id = await CreateProductAsync(client, fixtures, "Phone");

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{Products}/{id}/device",
            new
            {
                Device = "Model X",
                Colour = "Midnight",
                Battery = "5000mAh",
                Ram = "8GB",
                Storage = "256GB",
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        ProductDetail product = await ReadAsync(client, id);
        product.Device.ShouldBe("Model X");
        product.Storage.ShouldBe("256GB");
    }

    [Fact]
    public async Task An_unknown_product_is_not_found_and_an_anonymous_caller_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        (await client.GetAsync($"{Products}/{Guid.NewGuid()}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        HttpClient anonymous = _factory.CreateAnonymousClient();
        (await anonymous.GetAsync(Products)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<ProductDetail> ReadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ProductDetail>($"{Products}/{id}"))!;

    private static async Task<IReadOnlyList<ProductSummary>> ListAsync(
        HttpClient client,
        string search,
        bool includeInactive = false) =>
        (await client.GetFromJsonAsync<IReadOnlyList<ProductSummary>>(
            $"{Products}?search={Uri.EscapeDataString(search)}"
            + $"&includeInactive={includeInactive}"))!;

    private static Task<HttpResponseMessage> PostProductAsync(
        HttpClient client,
        Fixtures fixtures,
        string description,
        string? code = null) =>
        client.PostAsJsonAsync(
            Products,
            new
            {
                Description = description,
                CategoryId = fixtures.CategoryId,
                StockUnitId = fixtures.EachId,
                Code = code,
            });

    private static async Task<Guid> CreateProductAsync(
        HttpClient client,
        Fixtures fixtures,
        string description,
        string? code = null)
    {
        HttpResponseMessage response = await PostProductAsync(
            client, fixtures, description, code);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> AddBarcodeAsync(
        HttpClient client,
        Guid productId,
        string barcode,
        decimal? cost = null,
        decimal? retailRate = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Products}/{productId}/barcodes",
            new { Barcode = barcode, Cost = cost, RetailRate = retailRate });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static Task<HttpResponseMessage> SetRatesAsync(
        HttpClient client,
        Guid productId,
        decimal cost,
        decimal retailRate,
        decimal maximumRetailPrice = 0m) =>
        client.PutAsJsonAsync(
            $"{Products}/{productId}/rates",
            new
            {
                CostingMethod = 1,
                Cost = cost,
                RetailRate = retailRate,
                MaximumRetailPrice = maximumRetailPrice,
            });

    private static Task<HttpResponseMessage> SetStockingAsync(
        HttpClient client,
        Guid productId,
        Guid purchaseUnitId,
        Guid salesUnitId,
        decimal reorderLevel = 0m,
        bool tracksBatches = false,
        bool tracksSerialNumbers = false,
        int? shelfLifeDays = null) =>
        client.PutAsJsonAsync(
            $"{Products}/{productId}/stocking",
            new
            {
                PurchaseUnitId = purchaseUnitId,
                SalesUnitId = salesUnitId,
                ReorderLevel = reorderLevel,
                TracksBatches = tracksBatches,
                TracksSerialNumbers = tracksSerialNumbers,
                ShelfLifeDays = shelfLifeDays,
            });

    /// <summary>The masters a product needs before it can exist.</summary>
    /// <param name="Suffix">The suffix keeping this test's codes to itself.</param>
    /// <param name="CategoryId">A category.</param>
    /// <param name="EachId">A base unit.</param>
    /// <param name="BoxId">A unit derived from it, so conversion is testable.</param>
    /// <param name="KilogramId">A unit in another group, so refusal is testable.</param>
    private sealed record Fixtures(
        string Suffix,
        Guid CategoryId,
        Guid EachId,
        Guid BoxId,
        Guid KilogramId)
    {
        internal static async Task<Fixtures> CreateAsync(HttpClient client)
        {
            // Version 4 rather than version 7: a version 7 identifier leads with a
            // millisecond timestamp, so two taken in the same instant share a prefix.
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            Guid categoryId = await PostAsync(
                client, "categories",
                new { Code = $"CAT{suffix}", Name = $"Widgets {suffix}" });

            Guid eachId = await PostAsync(
                client, "units", new { Code = $"EA{suffix}", Name = "Each" });

            Guid boxId = await PostAsync(
                client, "units",
                new
                {
                    Code = $"BX{suffix}",
                    Name = "Box of 24",
                    BaseUnitId = eachId,
                    ConversionFactor = 24m,
                });

            Guid kilogramId = await PostAsync(
                client, "units", new { Code = $"KG{suffix}", Name = "Kilogram" });

            return new Fixtures(suffix, categoryId, eachId, boxId, kilogramId);
        }

        private static async Task<Guid> PostAsync(
            HttpClient client,
            string resource,
            object body)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"{Inventory}/{resource}", body);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);

            return await response.Content.ReadFromJsonAsync<Guid>();
        }
    }
}
