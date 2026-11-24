using System.Collections.Concurrent;
using ERP.Application.Abstractions.Storage;
using ERP.SharedKernel.Files;
using ERP.SharedKernel.Results;

namespace ERP.Api.Tests;

/// <summary>A bucket in memory: stands in for Cloudflare R2 so a test can see what was stored.</summary>
public sealed class MemoryFileStore : IFileStore
{
    /// <summary>Where the links point.</summary>
    public const string LinkBase = "https://files.test/";

    private readonly ConcurrentDictionary<string, string> _objects = new(StringComparer.Ordinal);

    /// <summary>Gets what is in the bucket, by key.</summary>
    public IReadOnlyDictionary<string, string> Objects => _objects;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public Task<Result<string>> KeepAsync(string folder, string dataUrl, CancellationToken cancellationToken = default)
    {
        if (!dataUrl.StartsWith("data:", StringComparison.Ordinal) || !dataUrl.Contains(',', StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Failure<string>(Error.Validation("File.Unreadable", "Not a data URL.")));
        }

        string key = $"files/{folder}/{Guid.NewGuid():N}";
        _objects[key] = dataUrl;

        return Task.FromResult(Result.Success(StoredFile.For(key)));
    }

    /// <inheritdoc />
    public string? LinkFor(string? stored) =>
        StoredFile.KeyOf(stored) is { } key ? LinkBase + key : stored;

    /// <inheritdoc />
    public Task<string?> ReadAsDataUrlAsync(string? stored, CancellationToken cancellationToken = default) =>
        Task.FromResult(StoredFile.KeyOf(stored) is { } key ? _objects.GetValueOrDefault(key) : stored);

    /// <inheritdoc />
    public Task DeleteAsync(string? stored, CancellationToken cancellationToken = default)
    {
        if (StoredFile.KeyOf(stored) is { } key)
        {
            _objects.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>Whether a link points at something still in the bucket.</summary>
    /// <param name="link">The link.</param>
    /// <returns>True when it does.</returns>
    public bool Holds(string? link) =>
        link is not null
        && link.StartsWith(LinkBase, StringComparison.Ordinal)
        && _objects.ContainsKey(link[LinkBase.Length..]);
}
