using ERP.Application.Abstractions.Storage;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Platform.Settings;
using ERP.Domain.Inventory;
using ERP.Domain.Parties;
using ERP.Domain.Platform;
using ERP.Domain.Service;
using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Files;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Storage;

/// <summary>
/// Moves the files saved in the database before file storage was switched on - product,
/// party and return photographs, and the logos in the print settings - into the bucket.
/// </summary>
/// <remarks>
/// <para>
/// Runs once, in the background, a little after the API starts, so it never holds up the
/// start itself or its health check. A few records at a time, tenant by tenant, each
/// batch saved on its own: stopped half way, the next start carries on from where it was.
/// </para>
/// <para>
/// Never fatal. Whatever goes wrong is logged, the records it could not move stay in the
/// database as they were - still readable - and the next start tries them again.
/// </para>
/// </remarks>
public sealed partial class StoredFileMigration : BackgroundService
{
    private const int BatchSize = 20;
    private const string Embedded = "data:";

    private readonly IServiceScopeFactory _scopes;
    private readonly IFileStore _files;
    private readonly ITenantContext _tenantContext;
    private readonly FileStorageOptions _options;
    private readonly ILogger<StoredFileMigration> _logger;

    /// <summary>Initialises a new instance of the <see cref="StoredFileMigration"/> class.</summary>
    /// <param name="scopes">Creates a scope, and a database context, for each batch.</param>
    /// <param name="files">The file store.</param>
    /// <param name="tenantContext">The ambient tenant scope, set for each tenant in turn.</param>
    /// <param name="options">The storage settings.</param>
    /// <param name="logger">The logger.</param>
    public StoredFileMigration(
        IServiceScopeFactory scopes,
        IFileStore files,
        ITenantContext tenantContext,
        IOptions<FileStorageOptions> options,
        ILogger<StoredFileMigration> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopes = scopes;
        _files = files;
        _tenantContext = tenantContext;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Moves every file still in the database into file storage.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many records were moved.</returns>
    public async Task<int> MoveAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_files.IsEnabled)
        {
            return 0;
        }

        List<TenantId> tenants;

        using (IServiceScope scope = _scopes.CreateScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ErpDbContext>().Tenants
                .AsNoTracking()
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken);
        }

        int moved = 0;

        foreach (TenantId tenant in tenants)
        {
            using IDisposable scope = _tenantContext.BeginScope(tenant);

            moved += await MoveProductPhotosAsync(cancellationToken);
            moved += await MovePartyPhotosAsync(cancellationToken);
            moved += await MoveRmaPhotosAsync(cancellationToken);
            moved += await MoveSettingsAsync(cancellationToken);
        }

        return moved;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_files.IsEnabled || !_options.MoveExistingFiles)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            int moved = await MoveAllAsync(stoppingToken);

            if (moved > 0)
            {
                LogMoved(moved);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; the next start carries on.
        }
#pragma warning disable CA1031 // Moving old files is housekeeping; it must never take the API down.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogFailed(exception, S3FileStore.HintFor(exception) ?? exception.Message);
        }
    }

    private async Task<int> MoveProductPhotosAsync(CancellationToken cancellationToken)
    {
        HashSet<ProductPhotoId> skipped = [];
        int moved = 0;

        while (true)
        {
            using IServiceScope scope = _scopes.CreateScope();
            ErpDbContext context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

            List<ProductPhoto> batch = [.. (await context.ProductPhotos
                .Where(photo => photo.Content.StartsWith(Embedded)
                    || (photo.Thumbnail != null && photo.Thumbnail.StartsWith(Embedded)))
                .OrderBy(photo => photo.Id)
                .Take(BatchSize + skipped.Count)
                .ToListAsync(cancellationToken))
                .Where(photo => !skipped.Contains(photo.Id))
                .Take(BatchSize)];

            if (batch.Count == 0)
            {
                return moved;
            }

            List<string> stored = [];

            foreach (ProductPhoto photo in batch)
            {
                string folder = $"{photo.FirmId.Value}/products/{photo.ProductId.Value}";
                Result<string> content = await KeepAsync(folder, photo.Content, stored, cancellationToken);
                Result<string>? thumbnail = photo.Thumbnail is { } small
                    ? await KeepAsync(folder, small, stored, cancellationToken)
                    : null;

                if (content.IsFailure
                    || thumbnail is { IsFailure: true }
                    || photo.MoveTo(content.Value, thumbnail?.Value).IsFailure)
                {
                    skipped.Add(photo.Id);
                    LogSkipped("product photograph", photo.Id.Value);
                }
            }

            moved += await SaveAsync(context, stored, cancellationToken);
        }
    }

    private async Task<int> MovePartyPhotosAsync(CancellationToken cancellationToken)
    {
        HashSet<PartyPhotoId> skipped = [];
        int moved = 0;

        while (true)
        {
            using IServiceScope scope = _scopes.CreateScope();
            ErpDbContext context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

            List<PartyPhoto> batch = [.. (await context.PartyPhotos
                .Where(photo => photo.Content.StartsWith(Embedded))
                .OrderBy(photo => photo.Id)
                .Take(BatchSize + skipped.Count)
                .ToListAsync(cancellationToken))
                .Where(photo => !skipped.Contains(photo.Id))
                .Take(BatchSize)];

            if (batch.Count == 0)
            {
                return moved;
            }

            List<string> stored = [];

            foreach (PartyPhoto photo in batch)
            {
                Result<string> content = await KeepAsync(
                    $"{photo.FirmId.Value}/parties/{photo.LedgerId.Value}", photo.Content, stored, cancellationToken);

                if (content.IsFailure || photo.MoveTo(content.Value).IsFailure)
                {
                    skipped.Add(photo.Id);
                    LogSkipped("party photograph", photo.Id.Value);
                }
            }

            moved += await SaveAsync(context, stored, cancellationToken);
        }
    }

    private async Task<int> MoveRmaPhotosAsync(CancellationToken cancellationToken)
    {
        HashSet<RmaPhotoId> skipped = [];
        int moved = 0;

        while (true)
        {
            using IServiceScope scope = _scopes.CreateScope();
            ErpDbContext context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

            var batch = (await context.RmaPhotos
                .Where(photo => photo.Content.StartsWith(Embedded))
                .Join(context.Rmas, photo => photo.RmaId, rma => rma.Id, (photo, rma) => new { Photo = photo, rma.FirmId })
                .OrderBy(row => row.Photo.Id)
                .Take(BatchSize + skipped.Count)
                .ToListAsync(cancellationToken))
                .Where(row => !skipped.Contains(row.Photo.Id))
                .Take(BatchSize)
                .ToList();

            if (batch.Count == 0)
            {
                return moved;
            }

            List<string> stored = [];

            foreach (var row in batch)
            {
                Result<string> content = await KeepAsync(
                    $"{row.FirmId.Value}/rmas/{row.Photo.RmaId.Value}", row.Photo.Content, stored, cancellationToken);

                if (content.IsFailure || row.Photo.MoveTo(content.Value).IsFailure)
                {
                    skipped.Add(row.Photo.Id);
                    LogSkipped("return photograph", row.Photo.Id.Value);
                }
            }

            moved += await SaveAsync(context, stored, cancellationToken);
        }
    }

    private async Task<int> MoveSettingsAsync(CancellationToken cancellationToken)
    {
        HashSet<FirmSettingId> skipped = [];
        int moved = 0;

        while (true)
        {
            using IServiceScope scope = _scopes.CreateScope();
            ErpDbContext context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

            List<FirmSetting> batch = [.. (await context.FirmSettings
                .Where(setting => setting.Value.Contains("data:image/"))
                .OrderBy(setting => setting.Id)
                .Take(BatchSize + skipped.Count)
                .ToListAsync(cancellationToken))
                .Where(setting => !skipped.Contains(setting.Id))
                .Take(BatchSize)];

            if (batch.Count == 0)
            {
                return moved;
            }

            List<string> stored = [];

            foreach (FirmSetting setting in batch)
            {
                Result<StoredSetting> result = await SettingFiles.StoreAsync(
                    _files, setting.FirmId, setting.Key, setting.Value, setting.Value, cancellationToken);

                if (result.IsSuccess)
                {
                    stored.AddRange(result.Value.Added);
                }

                // Nothing that is still embedded can be moved: count it as skipped, or the
                // next batch would pick it up again for ever.
                if (result.IsFailure
                    || SettingFiles.HoldsPictures(result.Value.Value)
                    || setting.Replace(result.Value.Value).IsFailure)
                {
                    skipped.Add(setting.Id);
                    LogSkipped("setting", setting.Id.Value);
                }
            }

            moved += await SaveAsync(context, stored, cancellationToken);
        }
    }

    private async Task<Result<string>> KeepAsync(
        string folder,
        string value,
        List<string> stored,
        CancellationToken cancellationToken)
    {
        if (StoredFile.IsReference(value))
        {
            return Result.Success(value);
        }

        Result<string> kept = await _files.KeepAsync(folder, value, cancellationToken);

        if (kept.IsSuccess)
        {
            stored.Add(kept.Value);
        }

        return kept;
    }

    /// <summary>Saves a batch; if it cannot be saved, the files stored for it are deleted.</summary>
    private async Task<int> SaveAsync(ErpDbContext context, List<string> stored, CancellationToken cancellationToken)
    {
        bool saved = false;

        try
        {
            int rows = await context.SaveChangesAsync(cancellationToken);
            saved = true;
            return rows;
        }
        finally
        {
            if (!saved)
            {
                foreach (string file in stored)
                {
                    await _files.DeleteAsync(file, CancellationToken.None);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Moved {Count} records' files from the database to file storage")]
    private partial void LogMoved(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the {Kind} {Id} to file storage; it stays in the database")]
    private partial void LogSkipped(string kind, Guid id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Moving files to file storage stopped; the next start tries again. {Reason}")]
    private partial void LogFailed(Exception exception, string reason);
}
