using ERP.Infrastructure.Storage;
using ERP.Infrastructure.Tenancy;
using ERP.SharedKernel.Files;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Tests.Storage;

/// <summary>
/// The store against a real S3-compatible bucket. Runs only when one is named in the
/// environment - an R2 bucket kept for testing, or a local stand-in such as moto - so the
/// ordinary build needs no network.
/// </summary>
/// <remarks>
/// <c>ERP_TEST_S3_ENDPOINT</c>, <c>ERP_TEST_S3_BUCKET</c>, <c>ERP_TEST_S3_ACCESS_KEY_ID</c> and
/// <c>ERP_TEST_S3_SECRET_ACCESS_KEY</c>; the bucket must already exist.
/// </remarks>
public sealed class S3FileStoreTests
{
    private const string Picture =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public async Task A_picture_is_stored_linked_read_back_and_deleted()
    {
        string? endpoint = Environment.GetEnvironmentVariable("ERP_TEST_S3_ENDPOINT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(endpoint), "No test bucket configured (ERP_TEST_S3_ENDPOINT).");

        FileStorageOptions options = new()
        {
            Enabled = true,
            Endpoint = endpoint,
            Bucket = Environment.GetEnvironmentVariable("ERP_TEST_S3_BUCKET") ?? "erp-test",
            AccessKeyId = Environment.GetEnvironmentVariable("ERP_TEST_S3_ACCESS_KEY_ID") ?? "test",
            SecretAccessKey = Environment.GetEnvironmentVariable("ERP_TEST_S3_SECRET_ACCESS_KEY") ?? "test",
            Region = Environment.GetEnvironmentVariable("ERP_TEST_S3_REGION") ?? "auto",
        };

        AmbientTenantContext tenants = new();
        using MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });
        using S3FileStore store = new(Options.Create(options), tenants, cache, NullLogger<S3FileStore>.Instance);
        TenantId tenant = TenantId.NewId();
        using IDisposable scope = tenants.BeginScope(tenant);

        Result<string> kept = await store.KeepAsync("firm/products", Picture, TestContext.Current.CancellationToken);

        kept.IsSuccess.ShouldBeTrue();
        StoredFile.KeyOf(kept.Value).ShouldNotBeNull().ShouldStartWith($"files/{tenant.Value}/firm/products/");
        StoredFile.KeyOf(kept.Value).ShouldEndWith(".png");

        // The link works without any credentials: it is what a browser is given.
        string link = store.LinkFor(kept.Value).ShouldNotBeNull();
        using HttpClient browser = new();
        using HttpResponseMessage fetched = await browser.GetAsync(new Uri(link), TestContext.Current.CancellationToken);
        fetched.IsSuccessStatusCode.ShouldBeTrue(link);
        (await fetched.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken))
            .ShouldBe(Convert.FromBase64String(Picture[(Picture.IndexOf(',', StringComparison.Ordinal) + 1)..]));
        fetched.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");

        (await store.ReadAsDataUrlAsync(kept.Value, TestContext.Current.CancellationToken)).ShouldBe(Picture);

        await store.DeleteAsync(kept.Value, TestContext.Current.CancellationToken);
        cache.Clear();
        (await store.ReadAsDataUrlAsync(kept.Value, TestContext.Current.CancellationToken)).ShouldBeNull();

        // What is not a reference passes straight through.
        store.LinkFor(Picture).ShouldBe(Picture);
        (await store.ReadAsDataUrlAsync(Picture, TestContext.Current.CancellationToken)).ShouldBe(Picture);
    }

    [Fact]
    public async Task Switched_off_it_leaves_the_picture_where_it_was()
    {
        using MemoryCache cache = new(new MemoryCacheOptions());
        using S3FileStore store = new(
            Options.Create(new FileStorageOptions()), new AmbientTenantContext(), cache, NullLogger<S3FileStore>.Instance);

        store.IsEnabled.ShouldBeFalse();
        (await store.KeepAsync("firm/products", Picture, TestContext.Current.CancellationToken)).Value.ShouldBe(Picture);
        store.LinkFor(Picture).ShouldBe(Picture);
        store.LinkFor("s3:files/x.png").ShouldBeNull();
        await store.DeleteAsync("s3:files/x.png", TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Pasted_settings_are_read_as_they_were_meant()
    {
        FileStorageOptions cleaned = new FileStorageOptions
        {
            Enabled = true,
            Endpoint = " https://abc123.r2.cloudflarestorage.com/erp-files\n",
            Bucket = "'erp-files'",
            AccessKeyId = "\"key\" ",
            SecretAccessKey = " secret\n",
            Region = " ",
        }.Cleaned();

        cleaned.Endpoint.ShouldBe("https://abc123.r2.cloudflarestorage.com");
        cleaned.Bucket.ShouldBe("erp-files");
        cleaned.AccessKeyId.ShouldBe("key");
        cleaned.SecretAccessKey.ShouldBe("secret");
        cleaned.Region.ShouldBe("auto");
        cleaned.IsConfigured.ShouldBeTrue();
    }

    [Fact]
    public async Task A_bucket_that_cannot_be_reached_fails_the_save_instead_of_throwing()
    {
        using MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 100 });
        AmbientTenantContext tenants = new();
        using S3FileStore store = new(
            Options.Create(new FileStorageOptions
            {
                Enabled = true,
                Endpoint = "http://127.0.0.1:9",
                Bucket = "nowhere",
                AccessKeyId = "key",
                SecretAccessKey = "secret",
            }),
            tenants,
            cache,
            NullLogger<S3FileStore>.Instance);
        using IDisposable scope = tenants.BeginScope(TenantId.NewId());

        Result<string> kept = await store.KeepAsync("firm/products", Picture, TestContext.Current.CancellationToken);

        kept.IsFailure.ShouldBeTrue();
        kept.Error.Code.ShouldBe("File.StorageUnavailable");
    }
}
