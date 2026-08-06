using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Masters;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for the inventory masters, end to end through the real host.
/// </summary>
/// <remarks>
/// Mostly ordinary CRUD, and tested lightly where it is. What gets attention is the
/// handful of places these masters carry a rule: a unit that must convert to a base
/// and not to another derived unit, and a default warehouse that has to stay both
/// singular and usable. Those are the parts that would be quietly wrong.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class InventoryMasterEndpointTests
{
    private const string Inventory = "/api/v1/inventory";

    private readonly ApiFactory _factory;

    public InventoryMasterEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_base_unit_and_its_derivatives_form_one_group()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Suffix();

        Guid baseId = await CreateUnitAsync(client, $"NO{suffix}", "Number");
        await CreateUnitAsync(client, $"PACK{suffix}", "Pack of 12", baseId, 12m);
        await CreateUnitAsync(client, $"BOX{suffix}", "Box of 24", baseId, 24m);

        IReadOnlyList<UnitSummary> units = await ReadUnitsAsync(client);

        UnitSummary pack = units.Single(unit => unit.Code == $"PACK{suffix}");
        pack.BaseUnitId.ShouldBe(baseId);
        pack.BaseUnitCode.ShouldBe($"NO{suffix}");
        pack.ConversionFactor.ShouldBe(12m);

        // The base itself comes back with no parent, which is what a left join has to
        // preserve - an inner one would return only the derived units.
        UnitSummary each = units.Single(unit => unit.Code == $"NO{suffix}");
        each.BaseUnitId.ShouldBeNull();
        each.ConversionFactor.ShouldBe(1m);
    }

    [Fact]
    public async Task A_unit_cannot_be_derived_from_another_derived_unit()
    {
        // Refusing the chain is what keeps every conversion a single multiplication.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Suffix();

        Guid baseId = await CreateUnitAsync(client, $"KG{suffix}", "Kilogram");
        Guid packId = await CreateUnitAsync(client, $"SACK{suffix}", "Sack", baseId, 25m);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/units",
            new
            {
                Code = $"PALLET{suffix}",
                Name = "Pallet of sacks",
                BaseUnitId = packId,
                ConversionFactor = 40m,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_duplicate_unit_code_is_refused_with_a_conflict()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Suffix();

        await CreateUnitAsync(client, $"DUP{suffix}", "First");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/units", new { Code = $"DUP{suffix}", Name = "Second" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_unit_can_be_renamed_and_withdrawn_and_returned()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Suffix();

        Guid unitId = await CreateUnitAsync(client, $"TMP{suffix}", "Temporary");

        (await client.PutAsJsonAsync(
            $"{Inventory}/units/{unitId}", new { Name = "Renamed" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SetActiveAsync(client, "UnitOfMeasure", unitId, false))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Withdrawn, so gone from the default list and present in the full one.
        (await ReadUnitsAsync(client)).ShouldNotContain(unit => unit.Id == unitId);

        IReadOnlyList<UnitSummary> all = await ReadUnitsAsync(client, includeInactive: true);
        all.Single(unit => unit.Id == unitId).Name.ShouldBe("Renamed");

        await SetActiveAsync(client, "UnitOfMeasure", unitId, true);
        (await ReadUnitsAsync(client)).ShouldContain(unit => unit.Id == unitId);
    }

    [Fact]
    public async Task A_sub_class_reports_the_category_it_sits_beneath()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Suffix();

        Guid parentId = await CreateCategoryAsync(client, $"PHONE{suffix}", "Phones");
        await CreateCategoryAsync(client, $"ANDR{suffix}", "Android", parentId);

        IReadOnlyList<CategorySummary> categories =
            (await client.GetFromJsonAsync<IReadOnlyList<CategorySummary>>(
                $"{Inventory}/categories"))!;

        CategorySummary child = categories.Single(row => row.Code == $"ANDR{suffix}");
        child.ParentId.ShouldBe(parentId);
        child.ParentName.ShouldBe("Phones");

        // The parent itself has none, which the left join has to preserve.
        categories.Single(row => row.Code == $"PHONE{suffix}").ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task A_brand_carries_both_languages()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Suffix();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            $"{Inventory}/brands",
            new { Code = $"SAM{suffix}", Name = "Samsung", NameArabic = "سامسونج" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        IReadOnlyList<BrandSummary> brands =
            (await client.GetFromJsonAsync<IReadOnlyList<BrandSummary>>(
                $"{Inventory}/brands"))!;

        brands.Single(brand => brand.Code == $"SAM{suffix}").NameArabic.ShouldBe("سامسونج");
    }

    [Fact]
    public async Task The_first_warehouse_a_firm_creates_becomes_its_default()
    {
        // Otherwise every document refuses to fill itself in, and somebody has to find
        // the setting before they can enter anything at all.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        IReadOnlyList<WarehouseSummary> existing = await ReadWarehousesAsync(client);

        if (existing.Count == 0)
        {
            Guid firstId = await CreateWarehouseAsync(client, $"WH{Suffix()}", "First store");

            (await ReadWarehousesAsync(client))
                .Single(warehouse => warehouse.Id == firstId)
                .IsDefault.ShouldBeTrue();

            return;
        }

        // A firm that already has one keeps it, and a second does not steal the role.
        Guid secondId = await CreateWarehouseAsync(client, $"WH{Suffix()}", "Second store");

        (await ReadWarehousesAsync(client))
            .Single(warehouse => warehouse.Id == secondId)
            .IsDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task Promoting_a_warehouse_demotes_the_previous_default()
    {
        // A filtered unique index permits one default per firm, so the demotion has to
        // happen in the same transaction or the database refuses the promotion.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        Guid firstId = await CreateWarehouseAsync(client, $"WH{Suffix()}", "Store one");
        Guid secondId = await CreateWarehouseAsync(client, $"WH{Suffix()}", "Store two");

        (await client.PostAsJsonAsync(
            $"{Inventory}/warehouses/{firstId}/default", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync(
            $"{Inventory}/warehouses/{secondId}/default", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        IReadOnlyList<WarehouseSummary> warehouses = await ReadWarehousesAsync(client);

        warehouses.Count(warehouse => warehouse.IsDefault).ShouldBe(1);
        warehouses.Single(warehouse => warehouse.IsDefault).Id.ShouldBe(secondId);
    }

    [Fact]
    public async Task The_default_warehouse_cannot_be_withdrawn_while_it_holds_that_role()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        Guid keeperId = await CreateWarehouseAsync(client, $"WH{Suffix()}", "Keeper");
        await client.PostAsJsonAsync($"{Inventory}/warehouses/{keeperId}/default", new { });

        HttpResponseMessage refused = await SetActiveAsync(
            client, "Warehouse", keeperId, false);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_masters_refuse_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync($"{Inventory}/units")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{Inventory}/warehouses")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>A short suffix, so tests in one collection do not collide on codes.</summary>
    /// <remarks>
    /// Version 4 rather than version 7, and from the front rather than the back. A
    /// version 7 identifier leads with a millisecond timestamp, so the first characters
    /// of two created in the same instant are identical - which is exactly what two
    /// consecutive calls in one test are.
    /// </remarks>
    private static string Suffix() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<IReadOnlyList<UnitSummary>> ReadUnitsAsync(
        HttpClient client,
        bool includeInactive = false) =>
        (await client.GetFromJsonAsync<IReadOnlyList<UnitSummary>>(
            $"{Inventory}/units?includeInactive={includeInactive}"))!;

    private static async Task<IReadOnlyList<WarehouseSummary>> ReadWarehousesAsync(
        HttpClient client) =>
        (await client.GetFromJsonAsync<IReadOnlyList<WarehouseSummary>>(
            $"{Inventory}/warehouses"))!;

    private static async Task<Guid> CreateUnitAsync(
        HttpClient client,
        string code,
        string name,
        Guid? baseUnitId = null,
        decimal conversionFactor = 1m)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/units",
            new
            {
                Code = code,
                Name = name,
                BaseUnitId = baseUnitId,
                ConversionFactor = conversionFactor,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> CreateCategoryAsync(
        HttpClient client,
        string code,
        string name,
        Guid? parentId = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/categories",
            new { Code = code, Name = name, ParentId = parentId });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> CreateWarehouseAsync(
        HttpClient client,
        string code,
        string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/warehouses", new { Code = code, Name = name });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static Task<HttpResponseMessage> SetActiveAsync(
        HttpClient client,
        string kind,
        Guid id,
        bool isActive) =>
        client.PostAsJsonAsync($"{Inventory}/{kind}/{id}/active", new { IsActive = isActive });
}
