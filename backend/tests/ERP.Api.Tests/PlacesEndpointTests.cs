using System.Net;
using System.Net.Http.Json;
using ERP.Application.Parties;
using ERP.Application.Sales;
using ERP.Domain.Parties;

namespace ERP.Api.Tests;

/// <summary>
/// Countries with their currencies, the states of each, areas and routes - and the
/// customer details that are chosen from them.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PlacesEndpointTests
{
    private const string Parties = "/api/v1/parties";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="PlacesEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public PlacesEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_firm_starts_with_every_country_and_its_currency_and_the_states_it_needs()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        List<CountrySummary> countries = (await client.GetFromJsonAsync<List<CountrySummary>>(
            $"{Parties}/countries"))!;

        countries.Count.ShouldBeGreaterThan(200);
        CountrySummary qatar = countries.Single(country => country.Code == "QA");
        qatar.CurrencyCode.ShouldBe("QAR");
        qatar.NameArabic.ShouldNotBeNullOrWhiteSpace();
        countries.Single(country => country.Code == "IN").StateCount.ShouldBeGreaterThan(30);

        // States are listed by the country they belong to.
        List<StateSummary> states = (await client.GetFromJsonAsync<List<StateSummary>>(
            $"{Parties}/states?countryId={qatar.Id}"))!;

        states.ShouldContain(state => state.Code == "QA-DA" && state.Name == "Doha");
        states.ShouldAllBe(state => state.CountryId == qatar.Id);
    }

    [Fact]
    public async Task Countries_states_areas_and_routes_can_be_added_and_corrected()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..2].ToUpperInvariant();

        // Three letters at most, and not one already in the list.
        HttpResponseMessage duplicate = await client.PostAsJsonAsync($"{Parties}/countries", new
        {
            Code = "QA",
            Name = "Qatar again",
        });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        HttpResponseMessage created = await client.PostAsJsonAsync($"{Parties}/countries", new
        {
            Code = $"Z{tag}",
            Name = $"Test land {tag}",
            CurrencyCode = "tst",
            CurrencyName = "Test dollar",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid countryId = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage badCurrency = await client.PutAsJsonAsync($"{Parties}/countries/{countryId}", new
        {
            Name = $"Test land {tag}",
            CurrencyCode = "DOLLARS",
        });
        badCurrency.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        HttpResponseMessage state = await client.PostAsJsonAsync($"{Parties}/states", new
        {
            CountryId = countryId,
            Code = $"Z{tag}-01",
            Name = "North",
        });
        state.StatusCode.ShouldBe(HttpStatusCode.Created, await state.Content.ReadAsStringAsync());

        // A state's code is unique across the firm: a party records the code alone.
        HttpResponseMessage clash = await client.PostAsJsonAsync($"{Parties}/states", new
        {
            CountryId = countryId,
            Code = "QA-DA",
            Name = "Another Doha",
        });
        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        CountrySummary country = (await client.GetFromJsonAsync<List<CountrySummary>>(
            $"{Parties}/countries"))!.Single(row => row.Id == countryId);
        country.CurrencyCode.ShouldBe("TST");
        country.StateCount.ShouldBe(1);

        HttpResponseMessage area = await client.PostAsJsonAsync($"{Parties}/territories", new
        {
            Kind = TerritoryKind.Area,
            Name = $"Area {tag}",
        });
        area.StatusCode.ShouldBe(HttpStatusCode.Created, await area.Content.ReadAsStringAsync());

        (await client.PostAsJsonAsync($"{Parties}/territories", new { Kind = TerritoryKind.Area, Name = $"area {tag}" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The same name as a route is a different list.
        (await client.PostAsJsonAsync($"{Parties}/territories", new { Kind = TerritoryKind.Route, Name = $"Area {tag}" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_customer_records_where_they_are_their_numbers_and_a_photograph()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid area = await (await client.PostAsJsonAsync($"{Parties}/territories", new
        {
            Kind = TerritoryKind.Area,
            Name = $"Wakra {tag}",
        })).Content.ReadFromJsonAsync<Guid>();
        Guid route = await (await client.PostAsJsonAsync($"{Parties}/territories", new
        {
            Kind = TerritoryKind.Route,
            Name = $"Van {tag}",
        })).Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/sales/customers", new
        {
            Name = $"Profiled {tag}",
            TaxDetails = new { StateCode = "QA-WA" },
            OpeningBalance = 100m,
            Profile = new
            {
                City = "Al Wakrah",
                PinCode = "1234",
                Website = "example.qa",
                Fax = "4444",
                ContactPerson = "Ahmed",
                GstNumber = "GST1",
                TinNumber = "TIN1",
                CstNumber = "CST1",
                CountryCode = "qa",
                AreaId = area,
                RouteId = route,
                SendsSms = true,
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        CustomerResponse customer = (await created.Content.ReadFromJsonAsync<CustomerResponse>())!;

        customer.Profile.City.ShouldBe("Al Wakrah");
        customer.Profile.CountryCode.ShouldBe("QA");
        customer.Profile.AreaId.ShouldBe(area);
        customer.Profile.RouteId.ShouldBe(route);
        customer.Profile.SendsSms.ShouldBeTrue();
        customer.TaxDetails.StateCode.ShouldBe("QA-WA");

        // A route is not an area.
        HttpResponseMessage wrongKind = await client.PutAsJsonAsync($"/api/v1/sales/customers/{customer.CustomerId}", new
        {
            Name = customer.Name,
            Profile = new { AreaId = route },
        });
        wrongKind.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The opening amount can be corrected after the customer was created.
        HttpResponseMessage corrected = await client.PutAsJsonAsync($"/api/v1/sales/customers/{customer.CustomerId}", new
        {
            Name = customer.Name,
            OpeningBalance = 250m,
        });
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        CustomerResponse after = (await corrected.Content.ReadFromJsonAsync<CustomerResponse>())!;
        after.OpeningBalance.ShouldBe(250m);
        after.Profile.City.ShouldBe("Al Wakrah");

        // A photograph, kept apart from the customer list.
        (await client.GetFromJsonAsync<PartyPhotoResponse>(
            $"/api/v1/sales/customers/{customer.CustomerId}/photo"))!.Content.ShouldBeNull();

        const string picture = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQ==";
        (await client.PutAsJsonAsync($"/api/v1/sales/customers/{customer.CustomerId}/photo", new { Content = picture }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<PartyPhotoResponse>(
            $"/api/v1/sales/customers/{customer.CustomerId}/photo"))!.Content.ShouldBe(picture);

        (await client.PutAsJsonAsync($"/api/v1/sales/customers/{customer.CustomerId}/photo", new { Content = "not a picture" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.PutAsJsonAsync($"/api/v1/sales/customers/{customer.CustomerId}/photo", new { Content = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<PartyPhotoResponse>(
            $"/api/v1/sales/customers/{customer.CustomerId}/photo"))!.Content.ShouldBeNull();
    }
}
