using System.Globalization;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Parties;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads sales and purchases over a period, added up by what the caller asks.</summary>
/// <remarks>
/// Line by line from posted documents, so a filter on a product, brand, category, tax rate
/// or serial number narrows to the lines it names. A sale's cost is what its own stock
/// document took the goods out at, shared between the lines of one product by quantity;
/// a sale that moved nothing - a service - costs nothing as far as can be told.
/// </remarks>
public sealed class TransactionReportReader : ITransactionReportReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="TransactionReportReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public TransactionReportReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<TransactionReport> ReadAsync(
        FirmId firmId,
        GetTransactionReportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        bool sales = query.Document == TransactionReportDocument.Sales;

        List<Line> lines = sales
            ? await SalesLinesAsync(firmId, query, cancellationToken)
            : await PurchaseLinesAsync(firmId, query, cancellationToken);

        if (query.TaxPercentage is { } rate)
        {
            lines = [.. lines.Where(line => Math.Abs(line.TaxRate - rate) < 0.01m)];
        }

        // A bill's additions and deductions belong to the bill: they go on the rows that
        // are bills or made of whole bills, and nowhere a bill is split up.
        bool wholeBills = !query.NarrowsLines
            && query.GroupBy is not (TransactionReportGrouping.Item
                or TransactionReportGrouping.Brand
                or TransactionReportGrouping.Category);

        Dictionary<Guid, decimal> others = wholeBills
            ? await OthersAsync(sales, lines, cancellationToken)
            : [];

        Dictionary<Line, decimal>? costs = sales ? await CostsAsync(firmId, lines, cancellationToken) : null;

        Names names = await NamesAsync(lines, cancellationToken);

        List<TransactionReportRow> rows =
        [
            .. lines
                .GroupBy(line => KeyOf(query.GroupBy, line))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => Row(query.GroupBy, group.Key, [.. group], others, costs, names)),
        ];

        if (query.GroupBy is TransactionReportGrouping.Item
            or TransactionReportGrouping.Brand
            or TransactionReportGrouping.Category
            or TransactionReportGrouping.Salesman
            or TransactionReportGrouping.Party)
        {
            rows = [.. rows.OrderBy(row => row.Label, StringComparer.OrdinalIgnoreCase)];
        }

        var firm = await _context.Firms
            .Where(row => row.Id == firmId)
            .Select(row => new { row.BaseCurrency })
            .FirstOrDefaultAsync(cancellationToken);

        decimal? nothing = sales ? 0m : null;
        TransactionReportRow totals = lines.Count == 0
            ? new TransactionReportRow("total", string.Empty, null, 0, 0m, 0m, 0m, 0m, 0m, nothing, nothing, null)
            : Row(query.GroupBy, "total", lines, others, costs, names) with
            {
                Key = "total",
                Label = string.Empty,
                Detail = null,
            };

        return new TransactionReport(
            query.From,
            query.To,
            firm is null ? string.Empty : firm.BaseCurrency.Code,
            query.GroupBy,
            rows,
            totals);
    }

    private static string KeyOf(TransactionReportGrouping grouping, Line line) => grouping switch
    {
        TransactionReportGrouping.Month => line.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        TransactionReportGrouping.Day => line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TransactionReportGrouping.Bill => string.Create(
            CultureInfo.InvariantCulture, $"{line.Date:yyyy-MM-dd}|{line.Number}|{line.Bill}"),
        TransactionReportGrouping.Item => line.Product.ToString(),
        TransactionReportGrouping.Brand => line.Brand?.ToString() ?? string.Empty,
        TransactionReportGrouping.Category => line.Category.ToString(),
        TransactionReportGrouping.Salesman => line.Salesman?.ToString() ?? string.Empty,
        _ => line.Party.ToString(),
    };

    private static TransactionReportRow Row(
        TransactionReportGrouping grouping,
        string key,
        List<Line> lines,
        Dictionary<Guid, decimal> others,
        Dictionary<Line, decimal>? costs,
        Names names)
    {
        Line first = lines[0];
        List<Guid> bills = [.. lines.Select(line => line.Bill).Distinct()];

        decimal amount = Round(lines.Sum(line => line.Sign * line.Amount));
        decimal tax = Round(lines.Sum(line => line.Sign * line.Tax));
        decimal other = Round(bills.Sum(bill => others.GetValueOrDefault(bill)));
        decimal? cost = costs is null ? null : Round(lines.Sum(line => costs.GetValueOrDefault(line)));
        decimal? profit = cost is null ? null : amount - cost.Value;

        (string label, string? detail) = grouping switch
        {
            TransactionReportGrouping.Bill => (
                first.Number,
                $"{first.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} · {names.Ledger(first.Party)}"),
            TransactionReportGrouping.Item => (names.Product(first.Product).Name, names.Product(first.Product).Code),
            TransactionReportGrouping.Brand => (first.Brand is { } brand ? names.Brand(brand) : "—", null),
            TransactionReportGrouping.Category => (names.Category(first.Category), null),
            TransactionReportGrouping.Salesman => (first.Salesman is { } man ? names.Ledger(man) : "—", null),
            TransactionReportGrouping.Party => (names.Ledger(first.Party), null),
            _ => (key, null),
        };

        // A bill's row is known by the bill, which is what opens it; the rest by what
        // they add up.
        return new TransactionReportRow(
            grouping == TransactionReportGrouping.Bill ? first.Bill.ToString() : key,
            label,
            detail,
            bills.Count,
            lines.Sum(line => line.Sign * line.Quantity),
            amount,
            tax,
            other,
            amount + tax + other,
            cost,
            profit,
            profit is null || amount == 0m
                ? null
                : Math.Round(profit.Value * 100m / amount, 2, MidpointRounding.AwayFromZero));
    }

    private async Task<List<Line>> SalesLinesAsync(
        FirmId firmId,
        GetTransactionReportQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<SalesInvoice> invoices = _context.SalesInvoices.AsNoTracking()
            .Where(invoice => invoice.FirmId == firmId
                && invoice.Status == SalesInvoiceStatus.Posted
                && invoice.Date >= query.From
                && invoice.Date <= query.To);

        if (query.PartyLedgerId is { } party)
        {
            LedgerId id = LedgerId.From(party);
            invoices = invoices.Where(invoice => invoice.CustomerLedgerId == id);
        }

        if (query.SalesmanLedgerId is { } salesman)
        {
            LedgerId id = LedgerId.From(salesman);
            invoices = invoices.Where(invoice => invoice.SalesmanLedgerId == id);
        }

        if (query.Mode is { } mode)
        {
            invoices = invoices.Where(invoice => invoice.Mode == mode);
        }

        if (query.WarehouseId is { } warehouse)
        {
            WarehouseId id = WarehouseId.From(warehouse);
            invoices = invoices.Where(invoice => invoice.WarehouseId == id);
        }

        IQueryable<SalesInvoiceLine> lines = _context.SalesInvoiceLines.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.SerialNumber))
        {
            string number = query.SerialNumber.Trim();
            List<SalesInvoiceLineId> carrying = await (
                    from serial in _context.SalesInvoiceLineSerials.AsNoTracking()
                    join known in _context.SerialNumbers.AsNoTracking() on serial.SerialNumberId equals known.Id
                    where known.FirmId == firmId && known.Number == number
                    select serial.SalesInvoiceLineId)
                .ToListAsync(cancellationToken);

            lines = lines.Where(line => carrying.Contains(line.Id));
        }

        var rows = await (
                from line in lines
                join invoice in invoices on line.SalesInvoiceId equals invoice.Id
                join product in Products(query) on line.ProductId equals product.Id
                join customer in Parties(query) on invoice.CustomerLedgerId equals customer.Id
                select new
                {
                    Bill = invoice.Id,
                    invoice.Number,
                    invoice.Date,
                    invoice.Kind,
                    invoice.CustomerLedgerId,
                    invoice.SalesmanLedgerId,
                    Stock = invoice.StockDocumentId ?? invoice.DeliveryNoteId,
                    line.ProductId,
                    product.BrandId,
                    product.CategoryId,
                    line.StockQuantity,
                    Amount = line.TaxableAmount.Amount,
                    Tax = line.TaxAmount.Amount,
                })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new Line(
                row.Bill.Value,
                row.Number,
                row.Date,
                row.CustomerLedgerId.Value,
                row.SalesmanLedgerId?.Value,
                row.Stock?.Value,
                row.ProductId.Value,
                row.BrandId?.Value,
                row.CategoryId.Value,
                row.StockQuantity,
                row.Amount,
                row.Tax,
                row.Kind == SalesDocumentKind.Return ? -1 : 1)),
        ];
    }

    private async Task<List<Line>> PurchaseLinesAsync(
        FirmId firmId,
        GetTransactionReportQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<PurchaseInvoice> invoices = _context.PurchaseInvoices.AsNoTracking()
            .Where(invoice => invoice.FirmId == firmId
                && invoice.Status == PurchaseInvoiceStatus.Posted
                && invoice.Date >= query.From
                && invoice.Date <= query.To);

        if (query.PartyLedgerId is { } party)
        {
            LedgerId id = LedgerId.From(party);
            invoices = invoices.Where(invoice => invoice.SupplierLedgerId == id);
        }

        if (query.Mode is { } mode)
        {
            invoices = invoices.Where(invoice => invoice.Mode == mode);
        }

        if (query.WarehouseId is { } warehouse)
        {
            WarehouseId id = WarehouseId.From(warehouse);
            invoices = invoices.Where(invoice => invoice.WarehouseId == id);
        }

        IQueryable<PurchaseInvoiceLine> lines = _context.PurchaseInvoiceLines.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.SerialNumber))
        {
            string number = query.SerialNumber.Trim();
            // A purchase keeps the numbers as they were keyed in.
            List<PurchaseInvoiceLineId> carrying = await _context.PurchaseInvoiceLineSerials.AsNoTracking()
                .Where(serial => serial.SerialNumber == number)
                .Select(serial => serial.PurchaseInvoiceLineId)
                .ToListAsync(cancellationToken);

            lines = lines.Where(line => carrying.Contains(line.Id));
        }

        var rows = await (
                from line in lines
                join invoice in invoices on line.PurchaseInvoiceId equals invoice.Id
                join product in Products(query) on line.ProductId equals product.Id
                join supplier in Parties(query) on invoice.SupplierLedgerId equals supplier.Id
                select new
                {
                    Bill = invoice.Id,
                    invoice.Number,
                    invoice.Date,
                    invoice.Kind,
                    invoice.SupplierLedgerId,
                    line.ProductId,
                    product.BrandId,
                    product.CategoryId,
                    line.StockQuantity,
                    Amount = line.TaxableAmount.Amount,
                    Tax = line.TaxAmount.Amount,
                })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new Line(
                row.Bill.Value,
                row.Number,
                row.Date,
                row.SupplierLedgerId.Value,
                null,
                null,
                row.ProductId.Value,
                row.BrandId?.Value,
                row.CategoryId.Value,
                row.StockQuantity,
                row.Amount,
                row.Tax,
                row.Kind == PurchaseDocumentKind.Return ? -1 : 1)),
        ];
    }

    /// <summary>The products a report reaches, narrowed to one, a brand or a category.</summary>
    private IQueryable<Product> Products(GetTransactionReportQuery query)
    {
        IQueryable<Product> products = _context.Products.AsNoTracking();

        if (query.ProductId is { } product)
        {
            ProductId id = ProductId.From(product);
            products = products.Where(row => row.Id == id);
        }

        if (query.BrandId is { } brand)
        {
            BrandId id = BrandId.From(brand);
            products = products.Where(row => row.BrandId == id);
        }

        if (query.CategoryId is { } category)
        {
            CategoryId id = CategoryId.From(category);
            products = products.Where(row => row.CategoryId == id);
        }

        return products;
    }

    /// <summary>The customers or suppliers a report reaches, narrowed to an area or a route.</summary>
    private IQueryable<Ledger> Parties(GetTransactionReportQuery query)
    {
        IQueryable<Ledger> parties = _context.Ledgers.AsNoTracking();

        if (query.AreaId is { } area)
        {
            TerritoryId id = TerritoryId.From(area);
            parties = parties.Where(row => row.AreaId == id);
        }

        if (query.RouteId is { } route)
        {
            TerritoryId id = TerritoryId.From(route);
            parties = parties.Where(row => row.RouteId == id);
        }

        return parties;
    }

    /// <summary>Each bill's additional ledgers, added and deducted, signed as the bill is.</summary>
    private async Task<Dictionary<Guid, decimal>> OthersAsync(
        bool sales,
        List<Line> lines,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, int> signs = lines
            .GroupBy(line => line.Bill)
            .ToDictionary(group => group.Key, group => group.First().Sign);

        if (signs.Count == 0)
        {
            return [];
        }

        List<(Guid Bill, decimal Amount, bool Adds)> charges;

        if (sales)
        {
            List<SalesInvoiceId> ids = [.. signs.Keys.Select(SalesInvoiceId.From)];
            var found = await _context.SalesInvoiceCharges.AsNoTracking()
                .Where(charge => ids.Contains(charge.SalesInvoiceId))
                .Select(charge => new { charge.SalesInvoiceId, charge.Amount.Amount, charge.IsAddition })
                .ToListAsync(cancellationToken);
            charges = [.. found.Select(charge => (charge.SalesInvoiceId.Value, charge.Amount, charge.IsAddition))];
        }
        else
        {
            List<PurchaseInvoiceId> ids = [.. signs.Keys.Select(PurchaseInvoiceId.From)];
            var found = await _context.PurchaseInvoiceCharges.AsNoTracking()
                .Where(charge => ids.Contains(charge.PurchaseInvoiceId))
                .Select(charge => new { charge.PurchaseInvoiceId, charge.Amount.Amount, charge.IsAddition })
                .ToListAsync(cancellationToken);
            charges = [.. found.Select(charge => (charge.PurchaseInvoiceId.Value, charge.Amount, charge.IsAddition))];
        }

        return charges
            .GroupBy(charge => charge.Bill)
            .ToDictionary(
                group => group.Key,
                group => signs[group.Key] * group.Sum(charge => charge.Adds ? charge.Amount : -charge.Amount));
    }

    /// <summary>What each sales line's goods cost, from the stock its bill moved.</summary>
    private async Task<Dictionary<Line, decimal>> CostsAsync(
        FirmId firmId,
        List<Line> lines,
        CancellationToken cancellationToken)
    {
        List<StockDocumentId> documents =
            [.. lines.Where(line => line.Stock is not null).Select(line => StockDocumentId.From(line.Stock!.Value)).Distinct()];

        var entries = await _context.StockLedgerEntries.AsNoTracking()
            .Where(entry => entry.FirmId == firmId && documents.Contains(entry.DocumentId))
            .Select(entry => new { entry.DocumentId, entry.ProductId, entry.Quantity, Value = entry.Value.Amount })
            .ToListAsync(cancellationToken);

        // Out is negative on the stock ledger; a sale's quantity and cost are what went out,
        // and a return's come back in, so they read negative here.
        Dictionary<(Guid Document, Guid Product), (decimal Quantity, decimal Cost)> moved = entries
            .GroupBy(entry => (entry.DocumentId.Value, entry.ProductId.Value))
            .ToDictionary(
                group => group.Key,
                group => (-group.Sum(entry => entry.Quantity), -group.Sum(entry => entry.Value)));

        Dictionary<Line, decimal> costs = [];

        foreach (Line line in lines)
        {
            if (line.Stock is not { } document
                || !moved.TryGetValue((document, line.Product), out (decimal Quantity, decimal Cost) went)
                || went.Quantity == 0m)
            {
                continue;
            }

            costs[line] = went.Cost * (line.Sign * line.Quantity / went.Quantity);
        }

        return costs;
    }

    private async Task<Names> NamesAsync(List<Line> lines, CancellationToken cancellationToken)
    {
        List<ProductId> products = [.. lines.Select(line => ProductId.From(line.Product)).Distinct()];
        List<BrandId> brands = [.. lines.Where(line => line.Brand is not null).Select(line => BrandId.From(line.Brand!.Value)).Distinct()];
        List<CategoryId> categories = [.. lines.Select(line => CategoryId.From(line.Category)).Distinct()];
        List<LedgerId> ledgers =
        [
            .. lines.Select(line => line.Party)
                .Concat(lines.Where(line => line.Salesman is not null).Select(line => line.Salesman!.Value))
                .Distinct()
                .Select(LedgerId.From),
        ];

        var productRows = await _context.Products.AsNoTracking()
            .Where(row => products.Contains(row.Id))
            .Select(row => new { row.Id, row.Code, row.Description })
            .ToListAsync(cancellationToken);
        var brandRows = await _context.Brands.AsNoTracking()
            .Where(row => brands.Contains(row.Id))
            .Select(row => new { row.Id, row.Name })
            .ToListAsync(cancellationToken);
        var categoryRows = await _context.Categories.AsNoTracking()
            .Where(row => categories.Contains(row.Id))
            .Select(row => new { row.Id, row.Name })
            .ToListAsync(cancellationToken);
        var ledgerRows = await _context.Ledgers.AsNoTracking()
            .Where(row => ledgers.Contains(row.Id))
            .Select(row => new { row.Id, row.Name })
            .ToListAsync(cancellationToken);

        return new Names(
            productRows.ToDictionary(row => row.Id.Value, row => (row.Code, row.Description)),
            brandRows.ToDictionary(row => row.Id.Value, row => row.Name),
            categoryRows.ToDictionary(row => row.Id.Value, row => row.Name),
            ledgerRows.ToDictionary(row => row.Id.Value, row => row.Name));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>One line of a posted document, as the report adds it up.</summary>
    private sealed record Line(
        Guid Bill,
        string Number,
        DateOnly Date,
        Guid Party,
        Guid? Salesman,
        Guid? Stock,
        Guid Product,
        Guid? Brand,
        Guid Category,
        decimal Quantity,
        decimal Amount,
        decimal Tax,
        int Sign)
    {
        /// <summary>Gets the rate the line was taxed at.</summary>
        public decimal TaxRate => Amount == 0m ? 0m : Math.Round(Tax * 100m / Amount, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The names the rows are labelled with.</summary>
    private sealed record Names(
        Dictionary<Guid, (string Code, string Description)> Products,
        Dictionary<Guid, string> Brands,
        Dictionary<Guid, string> Categories,
        Dictionary<Guid, string> Ledgers)
    {
        public (string Code, string Name) Product(Guid id) =>
            Products.TryGetValue(id, out (string Code, string Description) found) ? (found.Code, found.Description) : (string.Empty, string.Empty);

        public string Brand(Guid id) => Brands.GetValueOrDefault(id) ?? "—";

        public string Category(Guid id) => Categories.GetValueOrDefault(id) ?? "—";

        public string Ledger(Guid id) => Ledgers.GetValueOrDefault(id) ?? "—";
    }
}
