using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ERP.Api.Controllers;
using ERP.Application.Abstractions.Storage;
using ERP.Application.Parties;
using ERP.Application.Platform.Data;
using ERP.Application.Platform.Settings;
using ERP.Domain.Inventory;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Storage;
using ERP.SharedKernel.Files;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Tests;

/// <summary>
/// Photographs and print logos kept in file storage - Cloudflare R2 in production, a
/// bucket in memory here - with only a reference to each left in the database.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FileStorageEndpointTests
{
    private const string Picture = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP8=";
    private const string Other = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    private const string Thumbnail = "data:image/jpeg;base64,/9j/thumb=";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="FileStorageEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public FileStorageEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Photographs_go_to_the_bucket_and_the_database_keeps_only_a_reference()
    {
        MemoryFileStore bucket = new();
        await using WebApplicationFactory<Program> host = WithBucket(bucket);
        HttpClient client = await SignedInAsync(host);
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string photos = $"/api/v1/inventory/products/{masters.ProductId}/photos";

        (await client.PutAsJsonAsync($"{photos}/1", new { Content = Picture, Thumbnail }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The database holds references; the pictures are in the bucket.
        ProductPhoto row = await ProductPhotoAsync(host, masters.ProductId, 1);
        StoredFile.IsReference(row.Content).ShouldBeTrue(row.Content);
        StoredFile.IsReference(row.Thumbnail).ShouldBeTrue(row.Thumbnail);
        bucket.Objects[StoredFile.KeyOf(row.Content)!].ShouldBe(Picture);
        bucket.Objects[StoredFile.KeyOf(row.Thumbnail)!].ShouldBe(Thumbnail);

        // The screen gets links it can show, for the photograph and the catalog thumbnail.
        PhotoSetResponse set = (await client.GetFromJsonAsync<PhotoSetResponse>(photos))!;
        bucket.Holds(set.Photos[0]).ShouldBeTrue(set.Photos[0]);
        set.Photos[1].ShouldBeNull();
        Dictionary<Guid, string> thumbnails = (await client.GetFromJsonAsync<Dictionary<Guid, string>>(
            "/api/v1/inventory/products/thumbnails"))!;
        bucket.Holds(thumbnails[masters.ProductId]).ShouldBeTrue();

        // Replaced, the old pictures go; removed, so does the new one.
        (await client.PutAsJsonAsync($"{photos}/1", new { Content = Other }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        bucket.Objects.ShouldNotContainKey(StoredFile.KeyOf(row.Content)!);
        bucket.Objects.ShouldNotContainKey(StoredFile.KeyOf(row.Thumbnail)!);
        ProductPhoto replaced = await ProductPhotoAsync(host, masters.ProductId, 1);
        bucket.Objects[StoredFile.KeyOf(replaced.Content)!].ShouldBe(Other);
        replaced.Thumbnail.ShouldBeNull();

        (await client.PutAsJsonAsync($"{photos}/1", new { Content = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        bucket.Objects.ShouldNotContainKey(StoredFile.KeyOf(replaced.Content)!);

        // Something that is not a picture is still refused, and nothing is stored for it.
        int before = bucket.Objects.Count;
        (await client.PutAsJsonAsync($"{photos}/2", new { Content = "hello" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        bucket.Objects.Count.ShouldBe(before);

        // A customer's photograph goes the same way.
        string party = $"/api/v1/accounting/ledgers/{masters.CustomerId}/photos";
        (await client.PutAsJsonAsync($"{party}/1", new { Content = Picture }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        PhotoSetResponse partySet = (await client.GetFromJsonAsync<PhotoSetResponse>(party))!;
        bucket.Holds(partySet.Photos[0]).ShouldBeTrue(partySet.Photos[0]);
        (await client.GetFromJsonAsync<PartyPhotoResponse>($"/api/v1/sales/customers/{masters.CustomerId}/photo"))!
            .Content.ShouldBe(partySet.Photos[0]);
    }

    [Fact]
    public async Task A_return_keeps_its_photographs_in_the_bucket()
    {
        MemoryFileStore bucket = new();
        await using WebApplicationFactory<Program> host = WithBucket(bucket);
        HttpClient client = await SignedInAsync(host);
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        const string Rmas = "/api/v1/service/rmas";

        HttpResponseMessage created = await client.PostAsJsonAsync(Rmas, new
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            Direction = 1,
            PartyLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Reason = "Cracked screen",
            Lines = new[] { new { masters.ProductId, Quantity = 1m } },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage added = await client.PostAsJsonAsync($"{Rmas}/{id}/photos", new { Content = Other, Caption = "Front" });
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        Guid photoId = await added.Content.ReadFromJsonAsync<Guid>();

        string link = (await client.GetFromJsonAsync<RmaPhotoContent>($"{Rmas}/{id}/photos/{photoId}"))!.Content;
        bucket.Holds(link).ShouldBeTrue(link);
        bucket.Objects[link[MemoryFileStore.LinkBase.Length..]].ShouldBe(Other);

        (await client.DeleteAsync($"{Rmas}/{id}/photos/{photoId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        bucket.Holds(link).ShouldBeFalse();
    }

    [Fact]
    public async Task A_print_logo_is_kept_in_the_bucket_and_read_back_as_it_was_saved()
    {
        MemoryFileStore bucket = new();
        await using WebApplicationFactory<Program> host = WithBucket(bucket);
        HttpClient client = await SignedInAsync(host);
        string key = $"/api/v1/settings/printing-{Guid.NewGuid():N}"[..40];
        string setup = $$$"""{"templates":[{"id":"a4","company":{"name":"شركة","logo":"{{{Picture}}}"}}],"copyLabels":true}""";

        (await client.PutAsJsonAsync(key, new { Value = setup })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Saved with a reference in place of the picture...
        string saved = await SettingValueAsync(host, key);
        saved.ShouldNotContain("data:image/");
        saved.ShouldContain("شركة");
        bucket.Objects.Values.ShouldContain(Picture);

        // ...and read back exactly as the screen wrote it.
        FirmSettingResponse read = (await client.GetFromJsonAsync<FirmSettingResponse>(key))!;
        read.Value.ShouldNotBeNull();
        read.Value.ShouldContain(Picture);

        // Saving the same picture again does not store it again.
        int count = bucket.Objects.Count;
        (await client.PutAsJsonAsync(key, new { read.Value })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        bucket.Objects.Count.ShouldBe(count);
        (await SettingValueAsync(host, key)).ShouldBe(saved);

        // A reference the setting never held is not accepted: nobody can point a logo at
        // a file that is not theirs.
        string borrowed = setup.Replace(Picture, "s3:files/someone-else/logo.png", StringComparison.Ordinal);
        (await client.PutAsJsonAsync(key, new { Value = borrowed })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SettingValueAsync(host, key)).ShouldNotContain("someone-else");

        // The logo it dropped is deleted.
        bucket.Objects.Values.ShouldNotContain(Picture);
    }

    [Fact]
    public async Task Pictures_saved_before_storage_was_switched_on_are_moved_across()
    {
        // Saved while storage was off: in the database, as a data URL.
        HttpClient plain = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(plain);
        string photos = $"/api/v1/inventory/products/{masters.ProductId}/photos";
        (await plain.PutAsJsonAsync($"{photos}/2", new { Content = Picture, Thumbnail }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        string key = $"/api/v1/settings/moved-{Guid.NewGuid():N}"[..40];
        (await plain.PutAsJsonAsync(key, new { Value = $$$"""{"company":{"logo":"{{{Other}}}"}}""" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        MemoryFileStore bucket = new();
        await using WebApplicationFactory<Program> host = WithBucket(bucket);

        (await host.Services.GetRequiredService<StoredFileMigration>().MoveAllAsync())
            .ShouldBeGreaterThan(0);

        ProductPhoto row = await ProductPhotoAsync(host, masters.ProductId, 2);
        bucket.Objects[StoredFile.KeyOf(row.Content)!].ShouldBe(Picture);
        bucket.Objects[StoredFile.KeyOf(row.Thumbnail)!].ShouldBe(Thumbnail);
        (await SettingValueAsync(host, key)).ShouldNotContain("data:image/");

        HttpClient client = await SignedInAsync(host);
        bucket.Holds((await client.GetFromJsonAsync<PhotoSetResponse>(photos))!.Photos[1]).ShouldBeTrue();
        (await client.GetFromJsonAsync<FirmSettingResponse>(key))!.Value.ShouldNotBeNull().ShouldContain(Other);

        // Run again, there is nothing left to move.
        (await host.Services.GetRequiredService<StoredFileMigration>().MoveAllAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Clearing_a_firms_data_deletes_its_photographs_from_the_bucket()
    {
        MemoryFileStore bucket = new();
        await using WebApplicationFactory<Program> host = WithBucket(bucket);
        HttpClient client = await SignedInAsync(host);
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        (await client.PutAsJsonAsync($"/api/v1/inventory/products/{masters.ProductId}/photos/1", new { Content = Picture }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        string key = StoredFile.KeyOf((await ProductPhotoAsync(host, masters.ProductId, 1)).Content)!;
        bucket.Objects.ShouldContainKey(key);

        HttpResponseMessage cleared = await client.PostAsJsonAsync(
            "/api/v1/platform/data/reset", new { Scope = DataResetScope.Everything, Confirmation = "MAIN" });
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK, await cleared.Content.ReadAsStringAsync());

        bucket.Objects.ShouldNotContainKey(key);
    }

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> host)
    {
        HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await ApiFactory.SignInAsync(client)).AccessToken);

        return client;
    }

    private static async Task<ProductPhoto> ProductPhotoAsync(WebApplicationFactory<Program> host, Guid productId, int slot)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ErpDbContext context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

        return await context.ProductPhotos.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(photo => photo.ProductId == ProductId.From(productId) && photo.Slot == slot);
    }

    private static async Task<string> SettingValueAsync(WebApplicationFactory<Program> host, string route)
    {
        string key = route[(route.LastIndexOf('/') + 1)..];
        using IServiceScope scope = host.Services.CreateScope();
        ErpDbContext context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

        return await context.FirmSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .SingleAsync();
    }

    private WebApplicationFactory<Program> WithBucket(MemoryFileStore bucket) =>
        _factory.WithWebHostBuilder(builder => builder
            .UseSetting("Erp:Storage:S3:MoveExistingFiles", "false")
            .ConfigureTestServices(services => services.AddSingleton<IFileStore>(bucket)));
}
