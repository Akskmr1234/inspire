using System.Net;
using System.Net.Http.Json;
using ERP.Application.Parties;

namespace ERP.Api.Tests;

/// <summary>Up to three photographs for a product, and for an employee, customer or supplier.</summary>
[Collection(ApiCollection.Name)]
public sealed class PhotoEndpointTests
{
    private const string Picture = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP8=";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="PhotoEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public PhotoEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_product_keeps_three_photographs_in_their_slots()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string photos = $"/api/v1/inventory/products/{masters.ProductId}/photos";

        foreach (int slot in new[] { 1, 3 })
        {
            (await client.PutAsJsonAsync($"{photos}/{slot}", new { Content = Picture }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        PhotoSetResponse set = (await client.GetFromJsonAsync<PhotoSetResponse>(photos))!;
        set.Photos.ShouldBe([Picture, null, Picture]);

        // A fourth is refused, and something that is not a picture is too.
        (await client.PutAsJsonAsync($"{photos}/4", new { Content = Picture }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"{photos}/2", new { Content = "hello" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Removed, the slot is empty again.
        (await client.PutAsJsonAsync($"{photos}/3", new { Content = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<PhotoSetResponse>(photos))!.Photos
            .ShouldBe([Picture, null, null]);
    }

    [Fact]
    public async Task A_party_keeps_three_photographs_and_the_first_is_its_card_photo()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string photos = $"/api/v1/accounting/ledgers/{masters.CustomerId}/photos";

        (await client.PutAsJsonAsync($"{photos}/1", new { Content = Picture }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"{photos}/2", new { Content = Picture }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<PhotoSetResponse>(photos))!.Photos
            .ShouldBe([Picture, Picture, null]);

        // The customer screen's single photograph is the first of them.
        PartyPhotoResponse card = (await client.GetFromJsonAsync<PartyPhotoResponse>(
            $"/api/v1/sales/customers/{masters.CustomerId}/photo"))!;
        card.Content.ShouldBe(Picture);
    }
}
