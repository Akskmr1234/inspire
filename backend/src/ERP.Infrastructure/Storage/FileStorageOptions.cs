namespace ERP.Infrastructure.Storage;

/// <summary>
/// Object storage for uploaded files: Cloudflare R2, or any S3-compatible bucket.
/// </summary>
/// <remarks>
/// Off unless switched on and fully configured, in which case files stay in the database
/// exactly as before.
/// </remarks>
public sealed class FileStorageOptions
{
    /// <summary>The configuration section: <c>Erp__Storage__S3__*</c> as environment variables.</summary>
    public const string SectionName = "Erp:Storage:S3";

    /// <summary>Gets or sets a value indicating whether files go to the bucket.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the S3 endpoint: <c>https://&lt;account id&gt;.r2.cloudflarestorage.com</c>
    /// for R2.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the bucket.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Gets or sets the access key id of an API token that can read and write the bucket.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets that token's secret access key.</summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the signing region; R2 takes <c>auto</c>.</summary>
    public string Region { get; set; } = "auto";

    /// <summary>Gets or sets the folder in the bucket every file goes under.</summary>
    public string KeyPrefix { get; set; } = "files";

    /// <summary>Gets or sets how long a link to a picture works, in minutes.</summary>
    public int LinkMinutes { get; set; } = 720;

    /// <summary>
    /// Gets or sets a public address for the bucket (a custom domain, or its r2.dev
    /// address), when it has one. Links then point there and never expire; left empty,
    /// every link is signed and short-lived, and the bucket stays private.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether files saved in the database before storage
    /// was switched on are moved to the bucket in the background.
    /// </summary>
    public bool MoveExistingFiles { get; set; } = true;

    /// <summary>
    /// A copy with the values as they were meant: without the spaces, line breaks or quotes
    /// a paste brings, and with the endpoint cut back to its address - Cloudflare shows it
    /// with the bucket on the end, and the bucket is given on its own.
    /// </summary>
    /// <returns>The cleaned copy.</returns>
    public FileStorageOptions Cleaned()
    {
        string endpoint = Clean(Endpoint);

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
        {
            endpoint = uri.GetLeftPart(UriPartial.Authority);
        }

        return new FileStorageOptions
        {
            Enabled = Enabled,
            Endpoint = endpoint,
            Bucket = Clean(Bucket),
            AccessKeyId = Clean(AccessKeyId),
            SecretAccessKey = Clean(SecretAccessKey),
            Region = Clean(Region) is { Length: > 0 } region ? region : "auto",
            KeyPrefix = Clean(KeyPrefix),
            LinkMinutes = LinkMinutes,
            PublicBaseUrl = Clean(PublicBaseUrl),
            MoveExistingFiles = MoveExistingFiles,
        };
    }

    /// <summary>Gets a value indicating whether storage is on and has everything it needs.</summary>
    public bool IsConfigured =>
        Enabled
        && Uri.TryCreate(Endpoint, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);

    private static string Clean(string? value) => (value ?? string.Empty).Trim().Trim('\'', '"').Trim();
}
