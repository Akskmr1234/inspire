using ERP.Application.Service;
using ERP.Domain.Accounting;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads return merchandise authorisations for the screens.</summary>
public sealed class RmaReader : IRmaReader
{
    /// <summary>The most returns one list returns; the filters narrow it further.</summary>
    private const int ListLimit = 500;

    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="RmaReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public RmaReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RmaSummary>> ListAsync(
        FirmId firmId,
        ListRmasQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Rma> rmas = _context.Rmas.Where(rma => rma.FirmId == firmId);

        if (query.Direction is { } direction)
        {
            rmas = rmas.Where(rma => rma.Direction == direction);
        }

        if (query.Status is { } status)
        {
            rmas = rmas.Where(rma => rma.Status == status);
        }

        if (query.From is { } from)
        {
            rmas = rmas.Where(rma => rma.Date >= from);
        }

        if (query.To is { } to)
        {
            rmas = rmas.Where(rma => rma.Date <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = $"%{query.Search.Trim()}%";

            rmas = rmas.Where(rma =>
                EF.Functions.ILike(rma.Number, term)
                || EF.Functions.ILike(rma.Reason, term)
                || (rma.InvoiceReference != null && EF.Functions.ILike(rma.InvoiceReference, term))
                || rma.Lines.Any(line => line.SerialNumber != null && EF.Functions.ILike(line.SerialNumber, term))
                || _context.Ledgers.Any(ledger =>
                    ledger.Id == rma.PartyLedgerId && EF.Functions.ILike(ledger.Name, term)));
        }

        List<Rma> rows = await rmas
            .Include(rma => rma.Lines)
            .OrderByDescending(rma => rma.Date)
            .ThenByDescending(rma => rma.Number)
            .Take(ListLimit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Lookups lookups = await LookupsAsync(rows, cancellationToken);

        return [.. rows.Select(rma => Summarise(rma, lookups))];
    }

    /// <inheritdoc />
    public async Task<RmaDetail?> FindAsync(FirmId firmId, RmaId id, CancellationToken cancellationToken = default)
    {
        Rma? rma = await _context.Rmas
            .Include(row => row.Lines)
            .Include(row => row.Events)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id && row.FirmId == firmId, cancellationToken);

        if (rma is null)
        {
            return null;
        }

        Lookups lookups = await LookupsAsync([rma], cancellationToken);

        List<ProductId> productIds = [.. rma.Lines.Select(line => line.ProductId).Distinct()];

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Code,
                product.Description,
                product.StockUnitId,
                product.TracksBatches,
            })
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        List<BatchId> batchIds = [.. rma.Lines.Select(line => line.BatchId).OfType<BatchId>().Distinct()];

        Dictionary<BatchId, string> batches = await _context.Batches
            .Where(batch => batchIds.Contains(batch.Id))
            .ToDictionaryAsync(batch => batch.Id, batch => batch.Number, cancellationToken);

        // Captions only; the pictures are fetched one at a time, as they are opened.
        List<RmaPhotoSummary> photos = await _context.RmaPhotos
            .Where(photo => photo.RmaId == rma.Id)
            .OrderBy(photo => photo.Id)
            .Select(photo => new RmaPhotoSummary(photo.Id.Value, photo.Caption))
            .ToListAsync(cancellationToken);

        // The history shows who took each step by the name they go by now.
        List<UserId> userIds = [.. rma.Events.Select(entry => entry.UserId).Distinct()];

        Dictionary<UserId, string> users = await _context.Users
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);

        string warehouse = await _context.Warehouses
            .Where(row => row.Id == rma.WarehouseId)
            .Select(row => row.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new RmaDetail(
            Summarise(rma, lookups),
            rma.WarehouseId.Value,
            warehouse,
            rma.SalesInvoiceId?.Value,
            rma.PurchaseInvoiceId?.Value,
            Linked(rma, lookups),
            rma.Narration,
            rma.InspectionNotes,
            rma.InspectedOn,
            rma.ReplacementDocumentId?.Value,
            rma.CreditNoteId?.Value,
            rma.DebitNoteId?.Value,
            [
                .. rma.Lines.OrderBy(line => line.LineNumber).Select(line =>
                {
                    var product = products.GetValueOrDefault(line.ProductId);

                    return new RmaLineDetail(
                        line.ProductId.Value,
                        product?.Code ?? string.Empty,
                        product?.Description ?? string.Empty,
                        product is null ? string.Empty : units.GetValueOrDefault(product.StockUnitId, string.Empty),
                        line.Quantity,
                        line.Reason,
                        line.SerialNumber,
                        product?.TracksBatches ?? false,
                        line.BatchId?.Value,
                        line.BatchId is { } batch ? batches.GetValueOrDefault(batch) : null);
                }),
            ],
            photos,
            [
                .. rma.Events
                    .OrderBy(entry => entry.At)
                    .Select(entry => new RmaEventDetail(
                        entry.Status,
                        entry.At,
                        users.GetValueOrDefault(entry.UserId) ?? entry.UserName,
                        entry.Note)),
            ]);
    }

    /// <inheritdoc />
    public Task<string?> FindPhotoAsync(
        FirmId firmId,
        RmaId id,
        RmaPhotoId photoId,
        CancellationToken cancellationToken = default) =>
        _context.RmaPhotos
            .Where(photo => photo.Id == photoId
                && photo.RmaId == id
                && _context.Rmas.Any(rma => rma.Id == id && rma.FirmId == firmId))
            .Select(photo => photo.Content)
            .FirstOrDefaultAsync(cancellationToken);

    private static string? Linked(Rma rma, Lookups lookups)
    {
        if (rma.SalesInvoiceId is { } sale)
        {
            return lookups.Sales.GetValueOrDefault(sale);
        }

        return rma.PurchaseInvoiceId is { } purchase ? lookups.Purchases.GetValueOrDefault(purchase) : null;
    }

    /// <summary>The number of the document that settled the return, whichever kind it was.</summary>
    private static string? Outcome(Rma rma, Lookups lookups)
    {
        if (rma.CreditNoteId is { } credit)
        {
            return lookups.Sales.GetValueOrDefault(credit);
        }

        if (rma.DebitNoteId is { } debit)
        {
            return lookups.Purchases.GetValueOrDefault(debit);
        }

        return rma.ReplacementDocumentId is { } transfer ? lookups.Documents.GetValueOrDefault(transfer) : null;
    }

    private static RmaSummary Summarise(Rma rma, Lookups lookups)
    {
        return new RmaSummary(
            rma.Id.Value,
            rma.Number,
            rma.Date,
            rma.Direction,
            rma.PartyLedgerId.Value,
            lookups.Parties.GetValueOrDefault(rma.PartyLedgerId, string.Empty),
            rma.InvoiceReference ?? Linked(rma, lookups),
            rma.Reason,
            rma.Lines.Sum(line => line.Quantity),
            rma.Status,
            rma.Condition,
            Outcome(rma, lookups));
    }

    private async Task<Lookups> LookupsAsync(List<Rma> rmas, CancellationToken cancellationToken)
    {
        List<LedgerId> parties = [.. rmas.Select(rma => rma.PartyLedgerId).Distinct()];

        List<SalesInvoiceId> sales =
        [
            .. rmas.SelectMany(rma => new[] { rma.SalesInvoiceId, rma.CreditNoteId }).OfType<SalesInvoiceId>(),
        ];

        List<PurchaseInvoiceId> purchases =
        [
            .. rmas.SelectMany(rma => new[] { rma.PurchaseInvoiceId, rma.DebitNoteId }).OfType<PurchaseInvoiceId>(),
        ];

        List<StockDocumentId> documents =
        [
            .. rmas.Select(rma => rma.ReplacementDocumentId).OfType<StockDocumentId>(),
        ];

        Dictionary<LedgerId, string> names = parties.Count == 0
            ? []
            : await _context.Ledgers
                .Where(ledger => parties.Contains(ledger.Id))
                .ToDictionaryAsync(ledger => ledger.Id, ledger => ledger.Name, cancellationToken);

        Dictionary<SalesInvoiceId, string> saleNumbers = sales.Count == 0
            ? []
            : await _context.SalesInvoices
                .Where(invoice => sales.Contains(invoice.Id))
                .ToDictionaryAsync(invoice => invoice.Id, invoice => invoice.Number, cancellationToken);

        Dictionary<PurchaseInvoiceId, string> purchaseNumbers = purchases.Count == 0
            ? []
            : await _context.PurchaseInvoices
                .Where(invoice => purchases.Contains(invoice.Id))
                .ToDictionaryAsync(invoice => invoice.Id, invoice => invoice.Number, cancellationToken);

        Dictionary<StockDocumentId, string> documentNumbers = documents.Count == 0
            ? []
            : await _context.StockDocuments
                .Where(document => documents.Contains(document.Id))
                .ToDictionaryAsync(document => document.Id, document => document.Number, cancellationToken);

        return new Lookups(names, saleNumbers, purchaseNumbers, documentNumbers);
    }

    private sealed record Lookups(
        Dictionary<LedgerId, string> Parties,
        Dictionary<SalesInvoiceId, string> Sales,
        Dictionary<PurchaseInvoiceId, string> Purchases,
        Dictionary<StockDocumentId, string> Documents);
}
