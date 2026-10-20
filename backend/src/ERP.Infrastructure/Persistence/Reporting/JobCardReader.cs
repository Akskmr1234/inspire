using ERP.Application.Service;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads service job cards for the screens.</summary>
public sealed class JobCardReader : IJobCardReader
{
    /// <summary>The most cards one list returns; the filters narrow it further.</summary>
    private const int ListLimit = 500;

    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="JobCardReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public JobCardReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobCardSummary>> ListAsync(
        FirmId firmId,
        ListJobCardsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<JobCard> cards = _context.JobCards.Where(card => card.FirmId == firmId);

        if (query.Status is { } status)
        {
            cards = cards.Where(card => card.Status == status);
        }

        if (query.From is { } from)
        {
            cards = cards.Where(card => card.Date >= from);
        }

        if (query.To is { } to)
        {
            cards = cards.Where(card => card.Date <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = $"%{query.Search.Trim()}%";

            cards = cards.Where(card =>
                EF.Functions.ILike(card.Number, term)
                || EF.Functions.ILike(card.CustomerName, term)
                || (card.Mobile != null && EF.Functions.ILike(card.Mobile, term))
                || EF.Functions.ILike(card.ItemName, term)
                || (card.ModelNumber != null && EF.Functions.ILike(card.ModelNumber, term))
                || (card.SerialNumber != null && EF.Functions.ILike(card.SerialNumber, term)));
        }

        List<JobCard> rows = await cards
            .Include(card => card.Lines)
            .OrderByDescending(card => card.Date)
            .ThenByDescending(card => card.Number)
            .Take(ListLimit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Lookups lookups = await LookupsAsync(rows, cancellationToken);

        return [.. rows.Select(card => Summarise(card, lookups))];
    }

    /// <inheritdoc />
    public async Task<JobCardDetail?> FindAsync(
        FirmId firmId,
        JobCardId id,
        CancellationToken cancellationToken = default)
    {
        JobCard? card = await _context.JobCards
            .Include(row => row.Lines)
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id && row.FirmId == firmId, cancellationToken);

        if (card is null)
        {
            return null;
        }

        Lookups lookups = await LookupsAsync([card], cancellationToken);

        List<ProductId> productIds = [.. card.Lines.Select(line => line.ProductId).Distinct()];

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
                product.ItemType,
                product.TracksBatches,
            })
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        List<BatchId> batchIds = [.. card.Lines.Select(line => line.BatchId).OfType<BatchId>().Distinct()];

        Dictionary<BatchId, string> batches = await _context.Batches
            .Where(batch => batchIds.Contains(batch.Id))
            .ToDictionaryAsync(batch => batch.Id, batch => batch.Number, cancellationToken);

        string warehouse = await _context.Warehouses
            .Where(row => row.Id == card.WarehouseId)
            .Select(row => row.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new JobCardDetail(
            Summarise(card, lookups),
            card.WarehouseId.Value,
            warehouse,
            card.CustomerLedgerId?.Value,
            card.CustomerLedgerId is { } account ? lookups.People.GetValueOrDefault(account) : null,
            new JobCardCustomer(
                card.CustomerName, card.Mobile, card.AlternateMobile, card.Email, card.AddressLine1, card.AddressLine2),
            card.ReferenceNumber,
            card.PaymentMode,
            new JobCardDevice(
                card.ItemName, card.BrandName, card.ModelNumber, card.Colour, card.SerialNumber, card.Accessories),
            new JobCardWork(
                card.ServiceType,
                card.Fault,
                card.Problem,
                card.Remarks,
                card.Notes,
                card.EstimatedOn,
                card.EstimatedTime,
                card.EstimatedCharge),
            card.SalesmanLedgerId?.Value,
            card.TechnicianLedgerId?.Value,
            new JobCardStages(
                card.ProcessingOn, card.ReadyOn, card.NotReadyOn, card.InformedOn, card.DeliveredOn, card.DeadOn),
            card.CancellationReason,
            [
                .. card.Lines.OrderBy(line => line.LineNumber).Select(line =>
                {
                    var product = products.GetValueOrDefault(line.ProductId);

                    return new JobCardLineDetail(
                        line.ProductId.Value,
                        product?.Code ?? string.Empty,
                        product?.Description ?? string.Empty,
                        product is null ? string.Empty : units.GetValueOrDefault(product.StockUnitId, string.Empty),
                        line.Quantity,
                        line.Rate,
                        decimal.Round(line.Quantity * line.Rate, 4),
                        line.Remarks,
                        product?.ItemType == ItemType.Stock,
                        product?.TracksBatches ?? false,
                        line.BatchId?.Value,
                        line.BatchId is { } batch ? batches.GetValueOrDefault(batch) : null);
                }),
            ]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobCardCustomerMatch>> FindCustomersAsync(
        FirmId firmId,
        string mobile,
        CancellationToken cancellationToken = default)
    {
        string term = $"%{mobile.Trim()}%";

        // The details the customer gave last time come first: they are what the counter
        // wrote down, and they carry the account the job was put on, if any.
        var earlier = await _context.JobCards
            .Where(card => card.FirmId == firmId && card.Mobile != null && EF.Functions.ILike(card.Mobile, term))
            .OrderByDescending(card => card.Date)
            .ThenByDescending(card => card.Number)
            .Select(card => new
            {
                card.CustomerLedgerId,
                card.CustomerName,
                card.Mobile,
                card.AlternateMobile,
                card.Email,
                card.AddressLine1,
                card.AddressLine2,
            })
            .Take(50)
            .ToListAsync(cancellationToken);

        List<JobCardCustomerMatch> matches =
        [
            .. earlier
                .DistinctBy(card => card.Mobile)
                .Select(card => new JobCardCustomerMatch(
                    card.CustomerLedgerId?.Value,
                    new JobCardCustomer(
                        card.CustomerName,
                        card.Mobile,
                        card.AlternateMobile,
                        card.Email,
                        card.AddressLine1,
                        card.AddressLine2))),
        ];

        var accounts = await _context.Ledgers
            .Where(ledger => ledger.FirmId == firmId
                && ledger.Kind == LedgerKind.Customer
                && ledger.IsActive
                && ((ledger.MobileNumber != null && EF.Functions.ILike(ledger.MobileNumber, term))
                    || (ledger.Phone != null && EF.Functions.ILike(ledger.Phone, term))))
            .OrderBy(ledger => ledger.Name)
            .Select(ledger => new
            {
                ledger.Id,
                ledger.Name,
                ledger.MobileNumber,
                ledger.Phone,
                ledger.Email,
                ledger.AddressLine1,
                ledger.AddressLine2,
            })
            .Take(10)
            .ToListAsync(cancellationToken);

        HashSet<string?> seen = [.. matches.Select(match => match.Customer.Mobile)];

        matches.AddRange(accounts
            .Where(account => !seen.Contains(account.MobileNumber ?? account.Phone))
            .Select(account => new JobCardCustomerMatch(
                account.Id.Value,
                new JobCardCustomer(
                    account.Name,
                    account.MobileNumber ?? account.Phone,
                    account.MobileNumber is null ? null : account.Phone,
                    account.Email,
                    account.AddressLine1,
                    account.AddressLine2))));

        return [.. matches.Take(10)];
    }

    private static JobCardSummary Summarise(JobCard card, Lookups lookups)
    {
        (string Number, SalesInvoiceStatus Status)? invoice =
            card.SalesInvoiceId is { } id && lookups.Invoices.TryGetValue(id, out var found) ? found : null;

        return new JobCardSummary(
            card.Id.Value,
            card.Number,
            card.Date,
            card.CustomerName,
            card.Mobile,
            card.ItemName,
            card.BrandName,
            card.ModelNumber,
            card.SerialNumber,
            card.Fault,
            card.TechnicianLedgerId is { } technician ? lookups.People.GetValueOrDefault(technician) : null,
            card.EstimatedOn,
            card.EstimatedCharge,
            decimal.Round(card.Lines.Sum(line => line.Quantity * line.Rate), 4),
            card.Status,
            card.SalesInvoiceId?.Value,
            invoice?.Number,
            invoice?.Status);
    }

    private async Task<Lookups> LookupsAsync(List<JobCard> cards, CancellationToken cancellationToken)
    {
        List<LedgerId> people =
        [
            .. cards
                .SelectMany(card => new[] { card.CustomerLedgerId, card.TechnicianLedgerId })
                .OfType<LedgerId>()
                .Distinct(),
        ];

        List<SalesInvoiceId> invoices = [.. cards.Select(card => card.SalesInvoiceId).OfType<SalesInvoiceId>()];

        Dictionary<LedgerId, string> names = people.Count == 0
            ? []
            : await _context.Ledgers
                .Where(ledger => people.Contains(ledger.Id))
                .ToDictionaryAsync(ledger => ledger.Id, ledger => ledger.Name, cancellationToken);

        Dictionary<SalesInvoiceId, (string Number, SalesInvoiceStatus Status)> bills = invoices.Count == 0
            ? []
            : (await _context.SalesInvoices
                .Where(invoice => invoices.Contains(invoice.Id))
                .Select(invoice => new { invoice.Id, invoice.Number, invoice.Status })
                .ToListAsync(cancellationToken))
                .ToDictionary(invoice => invoice.Id, invoice => (invoice.Number, invoice.Status));

        return new Lookups(names, bills);
    }

    private sealed record Lookups(
        Dictionary<LedgerId, string> People,
        Dictionary<SalesInvoiceId, (string Number, SalesInvoiceStatus Status)> Invoices);
}
