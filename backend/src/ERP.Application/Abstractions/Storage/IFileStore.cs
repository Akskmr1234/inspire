using ERP.SharedKernel.Results;

namespace ERP.Application.Abstractions.Storage;

/// <summary>
/// Where uploaded files are kept: pictures of products, parties and returns, and the
/// logos printed on documents.
/// </summary>
/// <remarks>
/// <para>
/// With object storage configured (Cloudflare R2, or any S3-compatible bucket) a file is
/// put in the bucket and the database keeps only a reference to it
/// (<see cref="ERP.SharedKernel.Files.StoredFile"/>). Without it, nothing changes: the
/// file stays in the database as the data URL it arrived as, exactly as before.
/// </para>
/// <para>
/// Every method accepts either form, so callers never need to know which is in use or
/// whether a record was saved before storage was switched on.
/// </para>
/// </remarks>
public interface IFileStore
{
    /// <summary>Gets a value indicating whether files go to object storage.</summary>
    bool IsEnabled { get; }

    /// <summary>Stores a file given as a data URL.</summary>
    /// <param name="folder">
    /// What it is, as a path under the tenant's own folder: <c>{firm}/products</c>, for one.
    /// </param>
    /// <param name="dataUrl">The file, as a data URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A reference to the stored file; the data URL unchanged when storage is off; or why
    /// it could not be read.
    /// </returns>
    Task<Result<string>> KeepAsync(string folder, string dataUrl, CancellationToken cancellationToken = default);

    /// <summary>What a browser can load to show a file.</summary>
    /// <param name="stored">A reference, or a data URL kept in the database.</param>
    /// <returns>
    /// A short-lived link for a reference, the data URL as it is, or null when there is
    /// nothing to show.
    /// </returns>
    string? LinkFor(string? stored);

    /// <summary>Reads a file back as a data URL.</summary>
    /// <param name="stored">A reference, or a data URL kept in the database.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The file as a data URL, or null when it cannot be found.</returns>
    Task<string?> ReadAsDataUrlAsync(string? stored, CancellationToken cancellationToken = default);

    /// <summary>Deletes a stored file. Anything that is not a reference is ignored.</summary>
    /// <param name="stored">The reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>Never fails: a file left behind costs a little space, not a lost save.</remarks>
    Task DeleteAsync(string? stored, CancellationToken cancellationToken = default);
}
