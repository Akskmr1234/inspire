using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Inventory.Masters;
using ERP.Application.Inventory.Stock;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Service;

/// <summary>Handles taking jobs in, working them, and withdrawing them.</summary>
public sealed class JobCardHandlers
    : ICommandHandler<CreateJobCardCommand, Guid>,
      ICommandHandler<UpdateJobCardCommand>,
      ICommandHandler<MarkJobCardStageCommand>,
      ICommandHandler<CancelJobCardCommand>,
      IQueryHandler<ListJobCardsQuery, IReadOnlyList<JobCardSummary>>,
      IQueryHandler<GetJobCardQuery, JobCardDetail>,
      IQueryHandler<FindJobCardCustomersQuery, IReadOnlyList<JobCardCustomerMatch>>
{
    private readonly IJobCardRepository _cards;
    private readonly IJobCardReader _reader;
    private readonly ISalesInvoiceRepository _invoices;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ILedgerRepository _ledgers;
    private readonly IInventoryMasterRepository _masters;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="JobCardHandlers"/> class.</summary>
    /// <param name="cards">The job card repository.</param>
    /// <param name="reader">The job card reader.</param>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public JobCardHandlers(
        IJobCardRepository cards,
        IJobCardReader reader,
        ISalesInvoiceRepository invoices,
        IProductRepository products,
        IBatchRepository batches,
        ILedgerRepository ledgers,
        IInventoryMasterRepository masters,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cards = cards;
        _reader = reader;
        _invoices = invoices;
        _products = products;
        _batches = batches;
        _ledgers = ledgers;
        _masters = masters;
        _numbering = numbering;
        _financialYears = financialYears;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateJobCardCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<Guid>(Error.Forbidden(
                "JobCard.NoFirmOrBranchSelected",
                "A firm and a branch must be selected to take a job in."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<Guid>(Error.BusinessRule(
                "JobCard.NoFinancialYear", $"No financial year covers {request.Date:yyyy-MM-dd}."));
        }

        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.WarehouseId), cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId || warehouse.IsTransit)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "JobCard.WarehouseNotFound", "No such godown in the selected firm."));
        }

        Result<string> number = await StockDocumentWriter.ReserveAsync(
            _numbering, _tenantContext.TenantId, DocumentTypes.JobCard, firmId, branchId, year,
            cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<Guid>(number.Error);
        }

        Result<JobCard> created = JobCard.Create(
            _tenantContext.TenantId, firmId, year, number.Value, request.Date, warehouse);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        Result filled = await FillAsync(created.Value, request.Card, cancellationToken);

        if (filled.IsFailure)
        {
            return Result.Failure<Guid>(filled.Error);
        }

        _cards.Add(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(UpdateJobCardCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<JobCard> found = await FindAsync(request.JobCardId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        JobCard card = found.Value;

        if (card.Status == JobCardStatus.Cancelled)
        {
            return Result.Failure(Error.BusinessRule(
                "JobCard.Cancelled", $"Job card '{card.Number}' was cancelled."));
        }

        Result filled = await FillAsync(card, request.Card, cancellationToken);

        if (filled.IsFailure)
        {
            return filled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(MarkJobCardStageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<JobCard> found = await FindAsync(request.JobCardId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Result marked = found.Value.MarkStage(request.Stage, request.On);

        if (marked.IsFailure)
        {
            return marked;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(CancelJobCardCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<JobCard> found = await FindAsync(request.JobCardId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        await ReleaseCancelledInvoiceAsync(found.Value, _invoices, cancellationToken);

        Result cancelled = found.Value.Cancel(request.Reason);

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JobCardSummary>>> Handle(
        ListJobCardsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is not { } firmId
            ? Result.Failure<IReadOnlyList<JobCardSummary>>(NoFirm())
            : Result.Success(await _reader.ListAsync(firmId, request, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<JobCardDetail>> Handle(GetJobCardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<JobCardDetail>(NoFirm());
        }

        JobCardDetail? found = await _reader.FindAsync(firmId, JobCardId.From(request.JobCardId), cancellationToken);

        return found is null ? Result.Failure<JobCardDetail>(NotFound()) : Result.Success(found);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JobCardCustomerMatch>>> Handle(
        FindJobCardCustomersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<JobCardCustomerMatch>>(NoFirm());
        }

        string mobile = request.Mobile?.Trim() ?? string.Empty;

        // Two digits match half the customer list; the counter is still typing.
        return mobile.Length < 3
            ? Result.Success<IReadOnlyList<JobCardCustomerMatch>>([])
            : Result.Success(await _reader.FindCustomersAsync(firmId, mobile, cancellationToken));
    }

    /// <summary>Lets a job whose invoice was cancelled be worked and billed again.</summary>
    /// <param name="card">The card.</param>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the card has been looked at.</returns>
    /// <remarks>
    /// Read from the invoice rather than pushed from its cancellation, so cancelling an
    /// invoice does not have to know that job cards exist.
    /// </remarks>
    internal static async Task ReleaseCancelledInvoiceAsync(
        JobCard card,
        ISalesInvoiceRepository invoices,
        CancellationToken cancellationToken)
    {
        if (card.SalesInvoiceId is { } invoiceId
            && await invoices.FindAsync(invoiceId, cancellationToken) is { Status: SalesInvoiceStatus.Cancelled })
        {
            card.ReleaseInvoice();
        }
    }

    internal static Error NoFirm() =>
        Error.Forbidden("JobCard.NoFirmSelected", "A firm must be selected to work with job cards.");

    internal static Error NotFound() =>
        Error.NotFound("JobCard.NotFound", "No such job card in the selected firm.");

    private async Task<Result> FillAsync(JobCard card, JobCardInput input, CancellationToken cancellationToken)
    {
        LedgerId?[] people =
        [
            Id(input.CustomerLedgerId), Id(input.SalesmanLedgerId), Id(input.TechnicianLedgerId),
        ];

        IReadOnlyDictionary<LedgerId, Ledger> ledgers = await _ledgers.GetManyAsync(
            people.OfType<LedgerId>().Distinct(), cancellationToken);

        if (people.Any(id => id is { } known && !ledgers.ContainsKey(known)))
        {
            return Result.Failure(Error.NotFound(
                "JobCard.LedgerNotFound", "The customer, salesman or technician no longer exists."));
        }

        Result customer = card.SetCustomer(
            input.Customer, Ledger(people[0]), input.ReferenceNumber, input.PaymentMode);

        if (customer.IsFailure)
        {
            return customer;
        }

        Result device = card.SetDevice(input.Device);

        if (device.IsFailure)
        {
            return device;
        }

        Result work = card.SetWork(input.Work, Ledger(people[1]), Ledger(people[2]));

        if (work.IsFailure)
        {
            return work;
        }

        await ReleaseCancelledInvoiceAsync(card, _invoices, cancellationToken);

        // Once billed, the parts and labour are the invoice's; the rest of the card can
        // still be brought up to date - the diagnosis, the stages, a note.
        return card.IsBillable ? await ReplaceLinesAsync(card, input.Lines, cancellationToken) : Result.Success();

        Ledger? Ledger(LedgerId? id) => id is { } known ? ledgers[known] : null;
    }

    private async Task<Result> ReplaceLinesAsync(
        JobCard card,
        IReadOnlyList<JobCardLineInput> lines,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. lines.Select(line => ProductId.From(line.ProductId)).Distinct()], cancellationToken);

        IReadOnlyDictionary<BatchId, Batch> batches = await _batches.GetManyAsync(
            [.. lines.Where(line => line.BatchId is not null).Select(line => BatchId.From(line.BatchId!.Value)).Distinct()],
            cancellationToken);

        Result cleared = card.ClearLines();

        if (cleared.IsFailure)
        {
            return cleared;
        }

        foreach (JobCardLineInput input in lines)
        {
            if (!products.TryGetValue(ProductId.From(input.ProductId), out Product? product))
            {
                return Result.Failure(Error.NotFound(
                    "JobCard.ProductNotFound", "A part or service on the card no longer exists."));
            }

            Batch? batch = input.BatchId is { } batchId ? batches.GetValueOrDefault(BatchId.From(batchId)) : null;

            Result<JobCardLine> added = card.AddLine(product, input.Quantity, input.Rate, input.Remarks, batch);

            if (added.IsFailure)
            {
                return added;
            }
        }

        return Result.Success();
    }

    private async Task<Result<JobCard>> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<JobCard>(NoFirm());
        }

        JobCard? card = await _cards.FindAsync(JobCardId.From(id), cancellationToken);

        return card is null || card.FirmId != firmId
            ? Result.Failure<JobCard>(NotFound())
            : Result.Success(card);
    }

    private static LedgerId? Id(Guid? value) => value is { } id ? LedgerId.From(id) : null;
}

/// <summary>Handles <see cref="BillJobCardCommand"/>.</summary>
/// <remarks>
/// What comes out is a <b>draft</b> invoice of the card's parts and labour at the card's
/// rates, taxed at each product's tax rate. Posting it is what issues the parts and puts
/// the job on the customer's account, exactly as any other sale; the card only remembers
/// which invoice it was.
/// </remarks>
public sealed class BillJobCardCommandHandler : ICommandHandler<BillJobCardCommand, SalesInvoiceResponse>
{
    private readonly IJobCardRepository _cards;
    private readonly ISalesInvoiceRepository _invoices;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ITaxSlabRepository _taxSlabs;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="BillJobCardCommandHandler"/> class.</summary>
    /// <param name="cards">The job card repository.</param>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="taxSlabs">The tax rate repository.</param>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public BillJobCardCommandHandler(
        IJobCardRepository cards,
        ISalesInvoiceRepository invoices,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ITaxSlabRepository taxSlabs,
        ILedgerRepository ledgers,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        IFirmRepository firms,
        ITenantContext tenantContext,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _cards = cards;
        _invoices = invoices;
        _masters = masters;
        _products = products;
        _batches = batches;
        _taxSlabs = taxSlabs;
        _ledgers = ledgers;
        _numbering = numbering;
        _financialYears = financialYears;
        _firms = firms;
        _tenantContext = tenantContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<SalesInvoiceResponse>> Handle(
        BillJobCardCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.Forbidden(
                "SalesInvoice.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before raising an invoice."));
        }

        JobCard? card = await _cards.FindAsync(JobCardId.From(request.JobCardId), cancellationToken);

        if (card is null || card.FirmId != firmId)
        {
            return Result.Failure<SalesInvoiceResponse>(JobCardHandlers.NotFound());
        }

        await JobCardHandlers.ReleaseCancelledInvoiceAsync(card, _invoices, cancellationToken);

        if (card.SalesInvoiceId is { } billed)
        {
            SalesInvoice? existing = await _invoices.FindAsync(billed, cancellationToken);

            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "JobCard.AlreadyBilled",
                $"Job card '{card.Number}' is already billed by '{existing?.Number}'."));
        }

        if (card.Status == JobCardStatus.Cancelled)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "JobCard.Cancelled", $"Job card '{card.Number}' was cancelled."));
        }

        if (card.Lines.Count == 0)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "JobCard.NothingToBill", $"Job card '{card.Number}' has no parts or labour to bill."));
        }

        if ((card.CustomerLedgerId ?? Id(request.CustomerLedgerId)) is not { } customerId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.Validation(
                "JobCard.CustomerAccountRequired",
                "Choose the customer account the job is billed to - the cash customer for a walk-in."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);
        DateOnly date = request.Date ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        FinancialYear? year = await _financialYears.FindContainingAsync(firmId, date, cancellationToken);
        Warehouse? warehouse = await _masters.FindWarehouseAsync(card.WarehouseId, cancellationToken);

        LedgerId?[] people = [customerId, card.SalesmanLedgerId];
        IReadOnlyDictionary<LedgerId, Ledger> ledgers = await _ledgers.GetManyAsync(
            people.OfType<LedgerId>().Distinct(), cancellationToken);

        if (firm is null || year is null || warehouse is null || !ledgers.TryGetValue(customerId, out Ledger? customer))
        {
            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "SalesInvoice.CannotRaise",
                $"No financial year covers {date:yyyy-MM-dd}, or the job's customer or godown no longer exists."));
        }

        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. card.Lines.Select(line => line.ProductId).Distinct()], cancellationToken);

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units = await _masters.GetUnitsAsync(
            [.. products.Values.Select(product => product.StockUnitId).Distinct()], cancellationToken);

        IReadOnlyDictionary<BatchId, Batch> batches = await _batches.GetManyAsync(
            [.. card.Lines.Select(line => line.BatchId).OfType<BatchId>().Distinct()], cancellationToken);

        Dictionary<TaxSlabId, decimal> rates = (await _taxSlabs.ListAsync(firmId, true, cancellationToken))
            .ToDictionary(slab => slab.Id, slab => slab.Percentage);

        Result<string> number = await StockDocumentWriter.ReserveAsync(
            _numbering, _tenantContext.TenantId, DocumentTypes.SalesInvoice, firmId, branchId, year,
            cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(number.Error);
        }

        TaxMode mode = firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax;

        Result<SalesInvoice> draft = SalesInvoice.CreateDraft(
            _tenantContext.TenantId, firmId, branchId, year, number.Value, date, customer, warehouse,
            mode, firm.BaseCurrency);

        if (draft.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(draft.Error);
        }

        SalesInvoice invoice = draft.Value;

        foreach (JobCardLine line in card.Lines.OrderBy(line => line.LineNumber))
        {
            Product product = products[line.ProductId];

            Result<TaxRate> taxRate = TaxRate.Create(
                product.TaxSlabId is { } slab ? rates.GetValueOrDefault(slab) : 0m);

            if (taxRate.IsFailure)
            {
                return Result.Failure<SalesInvoiceResponse>(taxRate.Error);
            }

            TaxAssessment assessment = TaxCalculator.Calculate(
                Money.Of(line.Quantity * line.Rate, invoice.Currency),
                taxRate.Value,
                SalesTaxContext.For(firm, customer, mode));

            Result<SalesInvoiceLine> added = invoice.AddLine(
                product,
                units[product.StockUnitId],
                line.Quantity,
                line.Quantity,
                line.Rate,
                assessment,
                line.BatchId is { } batch ? batches.GetValueOrDefault(batch) : null);

            if (added.IsFailure)
            {
                return Result.Failure<SalesInvoiceResponse>(added.Error);
            }
        }

        Result terms = invoice.SetSaleTerms(
            card.PaymentMode,
            card.SalesmanLedgerId is { } salesman ? ledgers.GetValueOrDefault(salesman) : null,
            null);

        if (terms.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(terms.Error);
        }

        Result details = invoice.SetDetails(card.Number, Narration(card));

        if (details.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(details.Error);
        }

        Result recorded = card.RecordInvoice(invoice.Id);

        if (recorded.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(recorded.Error);
        }

        _invoices.Add(invoice);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateSalesInvoiceCommandHandler.Describe(invoice));
    }

    private static LedgerId? Id(Guid? value) => value is { } id ? LedgerId.From(id) : null;

    /// <summary>What the invoice says the job was: the device, then the fault.</summary>
    private static string Narration(JobCard card)
    {
        string device = string.Join(
            ' ', new[] { card.ItemName, card.BrandName, card.ModelNumber }.Where(part => !string.IsNullOrWhiteSpace(part)));
        string text = card.SerialNumber is null ? device : $"{device} ({card.SerialNumber})";
        text = card.Fault is null ? text : $"{text} - {card.Fault}";

        return text.Length <= SalesInvoice.MaximumNarrationLength
            ? text
            : text[..SalesInvoice.MaximumNarrationLength];
    }
}
