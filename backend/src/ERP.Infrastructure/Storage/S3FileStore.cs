using System.Globalization;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ERP.Application.Abstractions.Storage;
using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Files;
using ERP.SharedKernel.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Storage;

/// <summary>
/// Keeps uploaded files in an S3-compatible bucket - Cloudflare R2 - when one is
/// configured, and leaves them in the database as data URLs when not.
/// </summary>
/// <remarks>
/// <para>
/// Every key is <c>{prefix}/{tenant}/{folder}/{id}.{extension}</c>, so one tenant's files
/// never share a folder with another's, and the tenant always comes from the ambient
/// scope rather than from anything a caller says.
/// </para>
/// <para>
/// R2 speaks the S3 protocol but not all of it: it does not take the streaming payload
/// signatures or the default checksums newer AWS SDKs send, so both are switched off.
/// </para>
/// </remarks>
public sealed partial class S3FileStore : IFileStore, IDisposable
{
    private const string CachePrefix = "stored-file:";
    private const int MaximumCachedLength = 1_000_000;

    private readonly FileStorageOptions _options;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<S3FileStore> _logger;
    private readonly Lazy<AmazonS3Client> _client;

    /// <summary>Initialises a new instance of the <see cref="S3FileStore"/> class.</summary>
    /// <param name="options">The storage settings.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="cache">Keeps files read back recently: a key never changes what it holds.</param>
    /// <param name="logger">The logger.</param>
    public S3FileStore(
        IOptions<FileStorageOptions> options,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ILogger<S3FileStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value.Cleaned();
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
        _client = new Lazy<AmazonS3Client>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.IsConfigured;

    private bool IsHttps => !_options.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<Result<string>> KeepAsync(
        string folder,
        string dataUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(dataUrl);

        if (!IsEnabled)
        {
            return Result.Success(dataUrl);
        }

        if (!DataUrl.TryParse(dataUrl, out DataUrl? file))
        {
            return Result.Failure<string>(Error.Validation(
                "File.Unreadable", "The file could not be read: it should be a data URL."));
        }

        string key = KeyFor(folder, file.Extension);
        using MemoryStream stream = new(file.Content, writable: false);

        try
        {
            await PutAsync(key, stream, file.MediaType, cancellationToken);
        }
        catch (Exception exception) when (
            exception is AmazonServiceException or HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            string reason = HintFor(exception) ?? exception.Message;
            LogWriteFailed(exception, key, reason);

            return Result.Failure<string>(Error.Unexpected(
                "File.StorageUnavailable", "The file could not be stored just now. Nothing was saved; try again."));
        }

        return Result.Success(StoredFile.For(key));
    }

    private async Task PutAsync(string key, MemoryStream stream, string mediaType, CancellationToken cancellationToken)
    {
        await _client.Value.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = stream,
                AutoCloseStream = false,
                ContentType = mediaType,

                // R2 takes neither streaming payload signatures nor the default checksums.
                // Unsigned payloads are allowed over HTTPS only - R2 always is; a local
                // stand-in on plain HTTP gets the payload signed whole instead.
                DisablePayloadSigning = IsHttps,
                DisableDefaultChecksumValidation = true,
                UseChunkEncoding = false,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public string? LinkFor(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        if (StoredFile.KeyOf(stored) is not { } key)
        {
            return stored;
        }

        if (!IsEnabled)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            string path = string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
            return $"{_options.PublicBaseUrl.TrimEnd('/')}/{path}";
        }

        return _client.Value.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(Math.Clamp(_options.LinkMinutes, 1, 7 * 24 * 60)),
            Protocol = IsHttps ? Protocol.HTTPS : Protocol.HTTP,
        });
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsDataUrlAsync(string? stored, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        if (StoredFile.KeyOf(stored) is not { } key)
        {
            return stored;
        }

        if (!IsEnabled)
        {
            return null;
        }

        string cacheKey = CachePrefix + key;

        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            using GetObjectResponse response = await _client.Value.GetObjectAsync(
                _options.Bucket, key, cancellationToken);
            using MemoryStream buffer = new();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);

            string mediaType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;

            string dataUrl = new DataUrl(mediaType, buffer.ToArray()).ToString();

            // Keys are never reused, so what one holds never goes stale; the expiry only
            // keeps the memory from holding files nobody has asked for in a while.
            if (dataUrl.Length <= MaximumCachedLength)
            {
                _cache.Set(cacheKey, dataUrl, new MemoryCacheEntryOptions
                {
                    Size = (dataUrl.Length / 4096) + 1,
                    SlidingExpiration = TimeSpan.FromHours(1),
                });
            }

            return dataUrl;
        }
        catch (AmazonS3Exception exception)
        {
            LogReadFailed(key, (int)exception.StatusCode, exception.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string? stored, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || StoredFile.KeyOf(stored) is not { } key)
        {
            return;
        }

        try
        {
            await _client.Value.DeleteObjectAsync(_options.Bucket, key, cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonServiceException or HttpRequestException or IOException)
        {
            LogDeleteFailed(key, exception.Message);
        }
    }

    /// <summary>What to do about a refusal from the bucket, in words a person setting it up can act on.</summary>
    /// <param name="exception">What went wrong.</param>
    /// <returns>The advice, or null when there is none to give.</returns>
    public static string? HintFor(Exception? exception) =>
        (exception as AmazonServiceException)?.ErrorCode switch
        {
            "InvalidAccessKeyId" => "Erp__Storage__S3__AccessKeyId is not a known key: use the S3 'Access Key ID' "
                + "shown when the R2 API token was created, not the token value.",
            "SignatureDoesNotMatch" => "Erp__Storage__S3__SecretAccessKey does not match the key id: use the S3 "
                + "'Secret Access Key' of the same R2 API token.",
            "AccessDenied" => "The key may not use the bucket: give the R2 API token 'Object Read & Write' on it.",
            "NoSuchBucket" => "There is no such bucket at Erp__Storage__S3__Endpoint: check the name, and for an "
                + "EU bucket use https://<account-id>.eu.r2.cloudflarestorage.com.",
            _ => null,
        };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    private string KeyFor(string folder, string extension)
    {
        string tenant = _tenantContext.TenantId.Value.ToString();
        string prefix = _options.KeyPrefix.Trim('/');
        string name = string.Create(
            CultureInfo.InvariantCulture, $"{Guid.CreateVersion7():N}.{extension}");

        return string.Join(
            '/',
            new[] { prefix, tenant, folder.Trim('/'), name }.Where(part => part.Length > 0));
    }

    private AmazonS3Client CreateClient()
    {
        AmazonS3Config config = new()
        {
            ServiceURL = _options.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = string.IsNullOrWhiteSpace(_options.Region) ? "auto" : _options.Region,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2,
        };

        return new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey), config);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not store file {Key}: {Reason}")]
    private partial void LogWriteFailed(Exception exception, string key, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read stored file {Key}: {Status} {Reason}")]
    private partial void LogReadFailed(string key, int status, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete stored file {Key}: {Reason}")]
    private partial void LogDeleteFailed(string key, string reason);
}
