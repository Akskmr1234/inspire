using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ERP.Application.Abstractions.Storage;
using ERP.SharedKernel.Files;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Platform.Settings;

/// <summary>
/// Pictures inside a firm's settings - the logo on its printed documents - kept in file
/// storage instead of in the setting itself.
/// </summary>
/// <remarks>
/// <para>
/// A setting is a JSON document the screen owns. On the way in, every picture in it
/// (a string that is an image data URL) is stored and replaced by a reference; on the way
/// out, every reference is replaced by the picture again. The screen sees exactly what it
/// saved and never needs to know where the picture went.
/// </para>
/// <para>
/// A reference sent in by a client is kept only when the setting already held it, so
/// nobody can make a setting point at a file that is not theirs.
/// </para>
/// </remarks>
public static class SettingFiles
{
    private const string ImagePrefix = "data:image/";

    private static readonly JsonSerializerOptions Output = new()
    {
        // Firm names and terms are often Arabic; keep them readable in the database.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Stores the pictures in a setting about to be saved.</summary>
    /// <param name="files">The file store.</param>
    /// <param name="firmId">The firm the setting belongs to.</param>
    /// <param name="key">The setting's key.</param>
    /// <param name="value">The value being saved.</param>
    /// <param name="previous">The value it replaces, if any.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The value to save, and which files it added and dropped; or why it failed.</returns>
    public static async Task<Result<StoredSetting>> StoreAsync(
        IFileStore files,
        FirmId firmId,
        string key,
        string value,
        string? previous,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(value);

        HashSet<string> before = ReferencesIn(previous);

        if (!MightHoldFiles(value) || ParseOrNull(value) is not { } document)
        {
            return Result.Success(new StoredSetting(value, [], [.. before]));
        }

        // What each picture already stored reads back as: the screen sends the same
        // picture back on every save, and it should not be stored again each time.
        Dictionary<string, string> known = new(StringComparer.Ordinal);

        if (files.IsEnabled && value.Contains(ImagePrefix, StringComparison.Ordinal))
        {
            foreach (string reference in before)
            {
                if (await files.ReadAsDataUrlAsync(reference, cancellationToken) is { } picture)
                {
                    known.TryAdd(picture, reference);
                }
            }
        }

        List<string> added = [];
        HashSet<string> kept = new(StringComparer.Ordinal);
        string folder = $"{firmId.Value}/settings/{key}";

        foreach (JsonValue node in Strings(document))
        {
            string text = node.GetValue<string>();

            if (StoredFile.IsReference(text))
            {
                if (before.Contains(text))
                {
                    kept.Add(text);
                }
                else
                {
                    node.ReplaceWith(string.Empty);
                }

                continue;
            }

            if (!files.IsEnabled || !text.StartsWith(ImagePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (known.TryGetValue(text, out string? existing))
            {
                kept.Add(existing);
                node.ReplaceWith(existing);
                continue;
            }

            Result<string> stored = await files.KeepAsync(folder, text, cancellationToken);

            if (stored.IsFailure)
            {
                foreach (string orphan in added)
                {
                    await files.DeleteAsync(orphan, cancellationToken);
                }

                return Result.Failure<StoredSetting>(stored.Error);
            }

            added.Add(stored.Value);
            known.TryAdd(text, stored.Value);
            node.ReplaceWith(stored.Value);
        }

        return Result.Success(new StoredSetting(
            document.ToJsonString(Output),
            added,
            [.. before.Where(reference => !kept.Contains(reference))]));
    }

    /// <summary>Puts the pictures back into a setting being read.</summary>
    /// <param name="files">The file store.</param>
    /// <param name="value">The value as saved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The value with each reference replaced by its picture.</returns>
    public static async Task<string> ResolveAsync(
        IFileStore files,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(value);

        if (!value.Contains(StoredFile.Prefix, StringComparison.Ordinal) || ParseOrNull(value) is not { } document)
        {
            return value;
        }

        bool changed = false;

        foreach (JsonValue node in Strings(document))
        {
            string text = node.GetValue<string>();

            if (StoredFile.IsReference(text))
            {
                string picture = await files.ReadAsDataUrlAsync(text, cancellationToken) ?? string.Empty;
                node.ReplaceWith(picture);
                changed = true;
            }
        }

        return changed ? document.ToJsonString(Output) : value;
    }

    /// <summary>Whether a value holds a picture that is not yet in file storage.</summary>
    /// <param name="value">The value.</param>
    /// <returns>True when it might.</returns>
    public static bool HoldsPictures(string? value) =>
        value is not null && value.Contains(ImagePrefix, StringComparison.Ordinal);

    private static bool MightHoldFiles(string value) =>
        value.Contains(ImagePrefix, StringComparison.Ordinal)
        || value.Contains(StoredFile.Prefix, StringComparison.Ordinal);

    private static HashSet<string> ReferencesIn(string? value)
    {
        HashSet<string> references = new(StringComparer.Ordinal);

        if (value is null
            || !value.Contains(StoredFile.Prefix, StringComparison.Ordinal)
            || ParseOrNull(value) is not { } document)
        {
            return references;
        }

        foreach (JsonValue node in Strings(document))
        {
            string text = node.GetValue<string>();

            if (StoredFile.IsReference(text))
            {
                references.Add(text);
            }
        }

        return references;
    }

    private static JsonNode? ParseOrNull(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every string in a document, collected first so they can be replaced.</summary>
    private static List<JsonValue> Strings(JsonNode root)
    {
        List<JsonValue> found = [];
        Stack<JsonNode> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case JsonObject obj:
                    foreach (JsonNode child in obj.Select(property => property.Value).OfType<JsonNode>())
                    {
                        pending.Push(child);
                    }

                    break;

                case JsonArray array:
                    foreach (JsonNode child in array.OfType<JsonNode>())
                    {
                        pending.Push(child);
                    }

                    break;

                // A bare string as the whole document has nothing to be replaced within.
                case JsonValue leaf when leaf.Parent is not null && leaf.GetValueKind() == JsonValueKind.String:
                    found.Add(leaf);
                    break;

                default:
                    break;
            }
        }

        return found;
    }
}

/// <summary>A setting's value ready to save, with the files it added and dropped.</summary>
/// <param name="Value">The value to save.</param>
/// <param name="Added">Files stored for it; delete them if the save fails.</param>
/// <param name="Dropped">Files it no longer uses; delete them once the save succeeds.</param>
public sealed record StoredSetting(string Value, IReadOnlyList<string> Added, IReadOnlyList<string> Dropped);
