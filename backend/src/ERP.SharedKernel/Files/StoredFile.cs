namespace ERP.SharedKernel.Files;

/// <summary>
/// A file kept in object storage (Cloudflare R2, or any S3-compatible bucket) rather
/// than in the database: the row keeps only this reference to it.
/// </summary>
/// <remarks>
/// A reference is <see cref="Prefix"/> followed by the object's key. Only the server
/// writes one, after it has stored the file itself; nothing a client sends is ever
/// accepted as a reference, so no one can point a record at somebody else's file.
/// </remarks>
public static class StoredFile
{
    /// <summary>What every reference begins with.</summary>
    public const string Prefix = "s3:";

    /// <summary>The longest a reference may be.</summary>
    public const int MaximumReferenceLength = 512;

    /// <summary>Whether a value is a reference to a stored file.</summary>
    /// <param name="value">The value.</param>
    /// <returns>True for a well-formed reference.</returns>
    public static bool IsReference(string? value) =>
        value is { Length: > 3 and <= MaximumReferenceLength }
        && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The object key a reference points at.</summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The key, or null when the value is not a reference.</returns>
    public static string? KeyOf(string? reference) =>
        IsReference(reference) ? reference![Prefix.Length..] : null;

    /// <summary>A reference to an object key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The reference.</returns>
    public static string For(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Prefix + key;
    }
}
