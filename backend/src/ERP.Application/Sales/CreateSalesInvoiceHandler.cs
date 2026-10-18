using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using MediatR;

namespace ERP.Application.Sales;

/// <summary>Handles <see cref="CreateSalesInvoiceCommand"/>.</summary>
/// <remarks>
/// <para>
/// Everything is loaded and checked before anything is built, for the reason the stock
/// documents load the same way: a product from another firm found halfway through would
/// leave a reserved invoice number burnt on a document that never existed, and a gap in a
/// sales sequence is exactly what an auditor asks about.
/// </para>
/// <para>
/// Tax is assessed here rather than on the aggregate. The engine answers a question about
/// jurisdictions - which heads apply, at what rate, and whether a supply crossed a state
/// line - and the invoice records what it answered, so a reprint years later shows the tax
/// that was charged rather than the tax today's rates would produce.
/// </para>
/// </remarks>
/// <remarks>
/// Also handles <see cref="UpdateSalesInvoiceCommand"/>, because correcting an invoice is
/// entering its lines again: the same products, units, batches, serials and tax, checked
/// the same way. Two copies of that would drift until an edit accepted what entry refused.
/// </remarks>
public sealed class CreateSalesInvoiceCommandHandler
    : ICommandHandler<CreateSalesInvoiceCommand, SalesInvoiceResponse>,
        ICommandHandler<UpdateSalesInvoiceCommand, SalesInvoiceResponse>
{
    private readonly ISalesInvoiceRepository _invoices;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ISerialNumberRepository _serials;
    private readonly IAdditionalLedgerRepository _charges;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ISalesOrderRepository _orders;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="CreateSalesInvoiceCommandHandler"/> class.</summary>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="serials">The serial-number repository.</param>
    /// <param name="charges">The additional-charge repository.</param>
    /// <param name="ledgers">The nominal ledger repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="orders">The sales order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="sender">
    /// Sends the reopening and the posting an edit wraps, which join this transaction.
    /// </param>
    public CreateSalesInvoiceCommandHandler(
        ISalesInvoiceRepository invoices,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ISerialNumberRepository serials,
        IAdditionalLedgerRepository charges,
        ILedgerRepository ledgers,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        IFirmRepository firms,
        ISalesOrderRepository orders,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        ISender sender)
    {
        _invoices = invoices;
        _masters = masters;
        _products = products;
        _batches = batches;
        _serials = serials;
        _charges = charges;
        _ledgers = ledgers;
        _numbering = numbering;
        _financialYears = financialYears;
        _firms = firms;
        _orders = orders;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _sender = sender;
    }

    /// <inheritdoc />
    public async Task<Result<SalesInvoiceResponse>> Handle(
        CreateSalesInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.Forbidden(
                "SalesInvoice.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before entering an invoice."));
        }

        Result<Context> loaded = await LoadAsync(request, firmId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(loaded.Error);
        }

        Context context = loaded.Value;

        Result<string> number = await ReserveAsync(
            request.Kind, firmId, branchId, context.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(number.Error);
        }

        TaxMode mode = request.Mode ?? DefaultModeFor(context.Firm);

        Result<SalesInvoice> draft = SalesInvoice.CreateDraft(
            _tenantContext.TenantId,
            firmId,
            branchId,
            context.Year,
            number.Value,
            request.Date,
            context.Customer,
            context.Warehouse,
            mode,
            context.Firm.BaseCurrency,
            request.Kind,
            request.ReturnsInvoiceId is { } returns ? SalesInvoiceId.From(returns) : null);

        if (draft.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(draft.Error);
        }

        SalesInvoice invoice = draft.Value;

        Result lined = AddLines(invoice, request, context, mode);

        if (lined.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(lined.Error);
        }

        Result charged = AddCharges(invoice, request, context);

        if (charged.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(charged.Error);
        }

        Result details = SetDetails(invoice, request, context);

        if (details.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(details.Error);
        }

        _invoices.Add(invoice);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.Post)
        {
            Result posted = await SendAsync(
                new PostSalesInvoiceCommand(invoice.Id.Value), cancellationToken);

            if (posted.IsFailure)
            {
                return Result.Failure<SalesInvoiceResponse>(posted.Error);
            }
        }

        return Result.Success(Describe(invoice));
    }

    /// <inheritdoc />
    public async Task<Result<SalesInvoiceResponse>> Handle(
        UpdateSalesInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.Forbidden(
                "SalesInvoice.NoFirmSelected",
                "A firm must be selected before correcting an invoice."));
        }

        SalesInvoiceId id = SalesInvoiceId.From(request.SalesInvoiceId);
        SalesInvoice? invoice = await _invoices.FindAsync(id, cancellationToken);

        if (invoice is null || invoice.FirmId != firmId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.NotFound(
                "SalesInvoice.NotFound", "That invoice does not exist in the selected firm."));
        }

        if (invoice.Status == SalesInvoiceStatus.Cancelled)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "SalesInvoice.Cancelled",
                $"'{invoice.Number}' has been cancelled and cannot be edited. Enter it again "
                + "as a new invoice."));
        }

        bool wasPosted = invoice.Status == SalesInvoiceStatus.Posted;

        if (wasPosted)
        {
            string reason = string.IsNullOrWhiteSpace(request.Reason)
                ? "corrected"
                : request.Reason.Trim();

            Result reopened = await SendAsync(
                new ReopenSalesInvoiceCommand(request.SalesInvoiceId, reason), cancellationToken);

            if (reopened.IsFailure)
            {
                return Result.Failure<SalesInvoiceResponse>(reopened.Error);
            }
        }

        // The kind is the document's own: a sale edited into a return would be a credit
        // note numbered in the invoice series.
        CreateSalesInvoiceCommand changes = request.Changes with { Kind = invoice.Kind };

        Result<Context> loaded = await LoadAsync(changes, firmId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(loaded.Error);
        }

        Context context = loaded.Value;
        TaxMode mode = changes.Mode ?? invoice.Mode;

        List<(ProductId Product, decimal Quantity)> before =
            [.. invoice.Lines.Select(line => (line.ProductId, line.Quantity))];

        Result revised = invoice.Revise(
            context.Year,
            changes.Date,
            context.Customer,
            context.Warehouse,
            mode,
            changes.ReturnsInvoiceId is { } returns ? SalesInvoiceId.From(returns) : null);

        if (revised.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(revised.Error);
        }

        Result lined = AddLines(invoice, changes, context, mode);

        if (lined.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(lined.Error);
        }

        Result charged = AddCharges(invoice, changes, context);

        if (charged.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(charged.Error);
        }

        Result details = SetDetails(invoice, changes, context);

        if (details.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(details.Error);
        }

        Result ordered = await ResyncOrderAsync(invoice, before, cancellationToken);

        if (ordered.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(ordered.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (wasPosted || request.Post)
        {
            Result posted = await SendAsync(
                new PostSalesInvoiceCommand(request.SalesInvoiceId), cancellationToken);

            if (posted.IsFailure)
            {
                return Result.Failure<SalesInvoiceResponse>(posted.Error);
            }
        }

        return Result.Success(Describe(invoice));
    }

    /// <summary>Describes an invoice as it now stands.</summary>
    /// <param name="invoice">The invoice.</param>
    /// <returns>Its figures.</returns>
    internal static SalesInvoiceResponse Describe(SalesInvoice invoice) =>
        new(
            invoice.Id.Value,
            invoice.Number,
            invoice.Status,
            invoice.Taxable.Amount,
            invoice.Tax.Amount,
            invoice.ChargeTotal.Amount,
            invoice.RoundingDifference.Amount,
            invoice.Total.Amount);

    /// <summary>Sets the header fields that are not part of the goods.</summary>
    private static Result SetDetails(
        SalesInvoice invoice,
        CreateSalesInvoiceCommand request,
        Context context)
    {
        Result details = invoice.SetDetails(request.ReferenceNumber, request.Narration);

        return details.IsFailure
            ? details
            : invoice.SetSaleTerms(request.PaymentMode, context.Salesman, context.BillingMan);
    }

    /// <summary>The mode a firm's invoices open in, from its own regime.</summary>
    /// <remarks>
    /// The business's answer of 2026-08-10: nobody is offered a mode that does not apply
    /// where they trade, and a non-tax sale is the exception somebody selects
    /// deliberately. A firm with no regime at all can only sell without tax.
    /// </remarks>
    private static TaxMode DefaultModeFor(Firm firm) =>
        firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax;

    /// <summary>Builds each line, converting its quantity and assessing its tax.</summary>
    private static Result AddLines(
        SalesInvoice invoice,
        CreateSalesInvoiceCommand request,
        Context context,
        TaxMode mode)
    {
        for (int index = 0; index < request.Lines.Count; index++)
        {
            SalesInvoiceLineInput input = request.Lines[index];
            Product product = context.Products[ProductId.From(input.ProductId)];
            UnitOfMeasure stockUnit = context.Units[product.StockUnitId];

            UnitOfMeasure entryUnit = input.UnitId is { } unitId
                ? context.Units[UnitOfMeasureId.From(unitId)]
                : stockUnit;

            // Refused rather than guessed at when the units measure different things:
            // four kilograms of something stocked in litres is not a quantity anything
            // can arrive at.
            Result<decimal> stockQuantity = UnitOfMeasure.Convert(
                input.Quantity, entryUnit, stockUnit);

            if (stockQuantity.IsFailure)
            {
                return Result.Failure(stockQuantity.Error);
            }

            Result<TaxRate> rate = TaxRate.Create(input.TaxPercentage);

            if (rate.IsFailure)
            {
                return Result.Failure(rate.Error);
            }

            Money taxable = Money.Of(
                (input.Quantity * input.Rate) - input.Discount, invoice.Currency);

            TaxAssessment assessment = TaxCalculator.Calculate(
                taxable, rate.Value, ContextFor(context, mode));

            // By the line's position, not by its product. Keyed by product, two lines of
            // one product shared the first line's batch and the first line's serials, so
            // a unit sold on line one was sold again on line two and the posting refused
            // it as already gone.
            Batch? batch = context.Batches.GetValueOrDefault(index);

            IReadOnlyCollection<SerialNumber> units =
                context.Serials.GetValueOrDefault(index) ?? [];

            Result<SalesInvoiceLine> added = invoice.AddLine(
                product,
                entryUnit,
                input.Quantity,
                stockQuantity.Value,
                input.Rate,
                assessment,
                batch,
                units,
                input.Discount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>The tax conditions this document is entered under.</summary>
    /// <remarks>
    /// Shared with the order, because it is one question - which heads apply to this
    /// customer - and two copies would eventually disagree. The place-of-supply comparison
    /// is what selects IGST over CGST plus SGST, and it is inert outside the GST regime.
    /// <para>
    /// Amounts are treated as tax-exclusive. The reverse-tax setting of section 9 is not
    /// built, and cannot be until the invoice's own line check tells inclusive entry from
    /// exclusive - recorded in the specification beside the rounding answer.
    /// </para>
    /// </remarks>
    private static TaxContext ContextFor(Context context, TaxMode mode) =>
        SalesTaxContext.For(context.Firm, context.Customer, mode);

    /// <summary>Adds the charges the caller entered.</summary>
    /// <remarks>
    /// Only what was entered. Section 9's <c>Default</c> flag makes a charge appear on the
    /// screen with the document; it does not put one on an invoice nobody typed an amount
    /// into, and the only seeded default - Round Off - is computed by the invoice itself
    /// rather than entered at all.
    /// </remarks>
    private static Result AddCharges(
        SalesInvoice invoice,
        CreateSalesInvoiceCommand request,
        Context context)
    {
        foreach (SalesInvoiceChargeInput input in request.Charges ?? [])
        {
            if (!context.Charges.TryGetValue(LedgerId.From(input.LedgerId), out AdditionalLedger? mapping))
            {
                return Result.Failure(Error.NotFound(
                    "SalesInvoice.ChargeNotMapped",
                    "That charge is not one this firm carries on a sales document."));
            }

            Result<SalesInvoiceCharge> added = invoice.AddCharge(mapping, input.Amount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>Loads and checks everything the lines name, before any of it is used.</summary>
    private async Task<Result<Context>> LoadAsync(
        CreateSalesInvoiceCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<Context>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}. Create one before "
                + "invoicing on that date."));
        }

        Ledger? customer = await _ledgers.FindAsync(
            LedgerId.From(request.CustomerLedgerId), cancellationToken);

        if (customer is null || customer.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesInvoice.CustomerNotFound",
                "That customer account is not in the selected firm."));
        }

        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.WarehouseId), cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesInvoice.WarehouseNotFound",
                "That warehouse is not in the selected firm."));
        }

        List<ProductId> productIds =
            [.. request.Lines.Select(line => ProductId.From(line.ProductId)).Distinct()];

        IReadOnlyDictionary<ProductId, Product> products =
            await _products.GetManyAsync(productIds, cancellationToken);

        foreach (ProductId id in productIds)
        {
            if (!products.TryGetValue(id, out Product? product) || product.FirmId != firmId)
            {
                return Result.Failure<Context>(Error.NotFound(
                    "SalesInvoice.ProductNotFound", $"Product {id} is not in the selected firm."));
            }

            if (!product.IsActive)
            {
                return Result.Failure<Context>(Error.BusinessRule(
                    "SalesInvoice.ProductWithdrawn",
                    $"'{product.Code}' has been withdrawn from use."));
            }
        }

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units =
            await _masters.GetUnitsAsync(
                [
                    .. request.Lines
                        .Where(line => line.UnitId is not null)
                        .Select(line => UnitOfMeasureId.From(line.UnitId!.Value))
                        .Concat(products.Values.Select(product => product.StockUnitId))
                        .Distinct(),
                ],
                cancellationToken);

        foreach (SalesInvoiceLineInput line in request.Lines)
        {
            if (line.UnitId is { } unitId
                && (!units.TryGetValue(UnitOfMeasureId.From(unitId), out UnitOfMeasure? unit)
                    || unit.FirmId != firmId))
            {
                return Result.Failure<Context>(Error.NotFound(
                    "SalesInvoice.UnitNotFound", $"Unit {unitId} is not in the selected firm."));
            }
        }

        Result<Dictionary<int, Batch>> batches = await ResolveBatchesAsync(
            request, firmId, cancellationToken);

        if (batches.IsFailure)
        {
            return Result.Failure<Context>(batches.Error);
        }

        Result<Dictionary<int, IReadOnlyCollection<SerialNumber>>> serials =
            await ResolveSerialsAsync(request, firmId, cancellationToken);

        if (serials.IsFailure)
        {
            return Result.Failure<Context>(serials.Error);
        }

        IReadOnlyList<AdditionalLedger> mapped = await _charges.ListForDocumentAsync(
            firmId, ChargeableDocument.Sales, cancellationToken);

        Result<Ledger?> salesman = await FindEmployeeAsync(
            request.SalesmanLedgerId, firmId, cancellationToken);

        if (salesman.IsFailure)
        {
            return Result.Failure<Context>(salesman.Error);
        }

        Result<Ledger?> billingMan = await FindEmployeeAsync(
            request.BillingManLedgerId, firmId, cancellationToken);

        if (billingMan.IsFailure)
        {
            return Result.Failure<Context>(billingMan.Error);
        }

        return Result.Success(new Context(
            firm,
            year,
            customer,
            warehouse,
            products,
            units,
            batches.Value,
            serials.Value,
            mapped.ToDictionary(charge => charge.LedgerId),
            salesman.Value,
            billingMan.Value));
    }

    /// <summary>Finds a salesman or billing man, who must be one of the firm's employees.</summary>
    private async Task<Result<Ledger?>> FindEmployeeAsync(
        Guid? ledgerId,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        if (ledgerId is not { } id)
        {
            return Result.Success<Ledger?>(null);
        }

        Ledger? employee = await _ledgers.FindAsync(LedgerId.From(id), cancellationToken);

        return employee is null || employee.FirmId != firmId || employee.Kind != LedgerKind.Employee
            ? Result.Failure<Ledger?>(Error.NotFound(
                "SalesInvoice.EmployeeNotFound",
                "That salesman or billing man is not an employee of the selected firm."))
            : Result.Success<Ledger?>(employee);
    }

    /// <summary>Finds the batch each line names, by identifier or by number.</summary>
    /// <remarks>
    /// No batch is opened here, unlike a receipt. Goods are sold out of a batch that
    /// already exists, and a sales line naming one that does not is a typo rather than a
    /// new batch of stock nobody bought.
    /// </remarks>
    private async Task<Result<Dictionary<int, Batch>>> ResolveBatchesAsync(
        CreateSalesInvoiceCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        Dictionary<int, Batch> resolved = [];

        List<BatchId> ids =
        [
            .. request.Lines.Where(line => line.BatchId is not null)
                .Select(line => BatchId.From(line.BatchId!.Value)).Distinct(),
        ];

        IReadOnlyDictionary<BatchId, Batch> byId = ids.Count == 0
            ? new Dictionary<BatchId, Batch>()
            : await _batches.GetManyAsync(ids, cancellationToken);

        List<(ProductId Product, string Number)> numbers =
        [
            .. request.Lines
                .Where(line => line.BatchId is null && !string.IsNullOrWhiteSpace(line.BatchNumber))
                .Select(line => (ProductId.From(line.ProductId), line.BatchNumber!.Trim()))
                .Distinct(),
        ];

        IReadOnlyDictionary<(ProductId Product, string Number), Batch> byNumber =
            numbers.Count == 0
                ? new Dictionary<(ProductId, string), Batch>()
                : await _batches.GetByNumbersAsync(firmId, numbers, cancellationToken);

        for (int index = 0; index < request.Lines.Count; index++)
        {
            SalesInvoiceLineInput line = request.Lines[index];
            Batch? batch = null;

            if (line.BatchId is { } id)
            {
                batch = byId.GetValueOrDefault(BatchId.From(id));
            }
            else if (!string.IsNullOrWhiteSpace(line.BatchNumber))
            {
                batch = byNumber.GetValueOrDefault(
                    (ProductId.From(line.ProductId), line.BatchNumber.Trim()));
            }

            if (batch is null && (line.BatchId is not null
                || !string.IsNullOrWhiteSpace(line.BatchNumber)))
            {
                return Result.Failure<Dictionary<int, Batch>>(Error.NotFound(
                    "SalesInvoice.BatchNotFound",
                    $"No batch '{line.BatchNumber ?? line.BatchId.ToString()}' exists for "
                    + "that product."));
            }

            if (batch is not null)
            {
                resolved[index] = batch;
            }
        }

        return Result.Success(resolved);
    }

    /// <summary>Finds the units each line names, by their numbers within the product.</summary>
    private async Task<Result<Dictionary<int, IReadOnlyCollection<SerialNumber>>>>
        ResolveSerialsAsync(
            CreateSalesInvoiceCommand request,
            FirmId firmId,
            CancellationToken cancellationToken)
    {
        Dictionary<int, IReadOnlyCollection<SerialNumber>> resolved = [];

        List<(ProductId Product, string Number)> wanted =
        [
            .. request.Lines
                .SelectMany(line => (line.SerialNumbers ?? [])
                    .Select(number => (ProductId.From(line.ProductId), number.Trim())))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Item2))
                .Distinct(),
        ];

        if (wanted.Count == 0)
        {
            return Result.Success(resolved);
        }

        IReadOnlyDictionary<(ProductId Product, string Number), SerialNumber> found =
            await _serials.GetByNumbersAsync(firmId, wanted, cancellationToken);

        for (int index = 0; index < request.Lines.Count; index++)
        {
            SalesInvoiceLineInput line = request.Lines[index];
            List<SerialNumber> units = [];

            foreach (string number in line.SerialNumbers ?? [])
            {
                if (!found.TryGetValue(
                    (ProductId.From(line.ProductId), number.Trim()), out SerialNumber? serial))
                {
                    return Result.Failure<Dictionary<int, IReadOnlyCollection<SerialNumber>>>(
                        Error.NotFound(
                            "SalesInvoice.SerialNotFound",
                            $"No unit numbered '{number}' exists for that product."));
                }

                units.Add(serial);
            }

            if (units.Count > 0)
            {
                resolved[index] = units;
            }
        }

        return Result.Success(resolved);
    }

    /// <summary>Puts the order's figures right after an invoice raised from it changes.</summary>
    /// <remarks>
    /// What the invoice took off the order before the edit is given back, and what it
    /// carries now is taken - by product, the way a cancellation gives it back, because an
    /// invoice line does not name the order line it came from. More than the order still
    /// has outstanding is taken as far as the order goes: the extra is a sale beyond the
    /// order, which is the customer's business, and refusing it would refuse the edit.
    /// An order somebody has closed is left as it is.
    /// </remarks>
    private async Task<Result> ResyncOrderAsync(
        SalesInvoice invoice,
        IReadOnlyList<(ProductId Product, decimal Quantity)> before,
        CancellationToken cancellationToken)
    {
        if (invoice.SalesOrderId is not { } orderId)
        {
            return Result.Success();
        }

        SalesOrder? order = await _orders.FindAsync(orderId, cancellationToken);

        if (order is null
            || order.Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.Completed))
        {
            return Result.Success();
        }

        Dictionary<SalesOrderLineId, decimal> released = Match(
            order.Lines, before, line => line.InvoicedQuantity);

        if (released.Count > 0)
        {
            Result given = order.ReleaseInvoiced(released);

            if (given.IsFailure)
            {
                return given;
            }
        }

        Dictionary<SalesOrderLineId, decimal> taken = Match(
            order.Lines,
            [.. invoice.Lines.Select(line => (line.ProductId, line.Quantity))],
            line => line.OutstandingQuantity);

        return taken.Count == 0 ? Result.Success() : order.RecordInvoiced(taken);
    }

    /// <summary>Spreads quantities of each product across the order lines that have room.</summary>
    private static Dictionary<SalesOrderLineId, decimal> Match(
        IReadOnlyList<SalesOrderLine> lines,
        IReadOnlyList<(ProductId Product, decimal Quantity)> wanted,
        Func<SalesOrderLine, decimal> room)
    {
        Dictionary<SalesOrderLineId, decimal> matched = [];
        Dictionary<SalesOrderLineId, decimal> left = lines.ToDictionary(line => line.Id, room);

        foreach ((ProductId product, decimal quantity) in wanted)
        {
            decimal remaining = quantity;

            foreach (SalesOrderLineId lineId in lines
                .Where(line => line.ProductId == product)
                .Select(line => line.Id))
            {
                if (remaining <= 0m)
                {
                    break;
                }

                decimal taken = Math.Min(remaining, left[lineId]);

                if (taken <= 0m)
                {
                    continue;
                }

                matched[lineId] = matched.GetValueOrDefault(lineId) + taken;
                left[lineId] -= taken;
                remaining -= taken;
            }
        }

        return matched;
    }

    /// <summary>Sends a command that joins this transaction, and hands back its failure.</summary>
    /// <remarks>
    /// A nested command that fails throws to unwind the transaction it joined; caught here,
    /// its failure becomes this command's, and this command's own rollback undoes the lot.
    /// </remarks>
    private async Task<Result> SendAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : notnull
    {
        try
        {
            object? response = await _sender.Send((object)command, cancellationToken);

            return response as Result ?? Result.Success();
        }
        catch (TransactionRollbackException rollback) when (rollback.Response is Result failed)
        {
            return failed;
        }
    }

    /// <summary>Takes the next number, creating the series if there is none.</summary>
    /// <remarks>
    /// A separate series per kind, which is what §11 describes and what a firm expects: a
    /// credit note is not a gap in the invoice sequence, and an auditor asking why
    /// SL/2026/0004 does not exist deserves a better answer than "it was a return".
    /// </remarks>
    private async Task<Result<string>> ReserveAsync(
        SalesDocumentKind kind,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        bool isReturn = kind == SalesDocumentKind.Return;
        string documentType = isReturn ? DocumentTypes.SalesReturn : DocumentTypes.SalesInvoice;

        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            documentType, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, documentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            NumberingDefaults.Apply(series, year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }

    /// <summary>Everything the lines name, loaded and checked in one pass.</summary>
    private sealed record Context(
        Firm Firm,
        FinancialYear Year,
        Ledger Customer,
        Warehouse Warehouse,
        IReadOnlyDictionary<ProductId, Product> Products,
        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> Units,
        IReadOnlyDictionary<int, Batch> Batches,
        IReadOnlyDictionary<int, IReadOnlyCollection<SerialNumber>> Serials,
        IReadOnlyDictionary<LedgerId, AdditionalLedger> Charges,
        Ledger? Salesman,
        Ledger? BillingMan);
}
