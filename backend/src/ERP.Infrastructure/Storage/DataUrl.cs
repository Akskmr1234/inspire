using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ERP.Infrastructure.Storage;

/// <summary>A file written out as a data URL: <c>data:image/png;base64,iVBOR...</c>.</summary>
/// <param name="MediaType">What the file is, such as <c>image/png</c>.</param>
/// <param name="Content">The file's bytes.</param>
public sealed record DataUrl(string MediaType, byte[] Content)
{
    private const string Scheme = "data:";

    /// <summary>Reads a data URL.</summary>
    /// <param name="value">The data URL.</param>
    /// <param name="dataUrl">The file, when it could be read.</param>
    /// <returns>Whether it could be read.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out DataUrl? dataUrl)
    {
        dataUrl = null;

        if (value is null || !value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int comma = value.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0)
        {
            return false;
        }

        string[] header = value[Scheme.Length..comma].Split(';');
        bool isBase64 = header.Skip(1).Any(part => part.Equals("base64", StringComparison.OrdinalIgnoreCase));
        string mediaType = string.IsNullOrWhiteSpace(header[0]) ? "text/plain" : header[0].Trim().ToLowerInvariant();
        string payload = value[(comma + 1)..];

        try
        {
            byte[] content = isBase64
                ? Convert.FromBase64String(payload)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

            dataUrl = new DataUrl(mediaType, content);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The usual file extension for the media type.</summary>
    public string Extension => MediaType switch
    {
        "image/jpeg" or "image/jpg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        "image/gif" => "gif",
        "image/svg+xml" => "svg",
        "image/avif" => "avif",
        "image/bmp" => "bmp",
        "application/pdf" => "pdf",
        _ => "bin",
    };

    /// <summary>Writes the file back out as a data URL.</summary>
    /// <returns>The data URL.</returns>
    public override string ToString() => $"{Scheme}{MediaType};base64,{Convert.ToBase64String(Content)}";
}
