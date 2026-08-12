using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using FluentValidation;

namespace ERP.Application.Sales;

/// <summary>One product on an invoice being entered.</summary>
/// <param name="ProductId">The product sold.</param>
/// <param name="Quantity">How many, in the unit named.</param>
/// <param name="Rate">What one of them goes for.</param>
/// <param name="TaxPercentage">
/// The rate tax is charged at. Supplied per line rather than read from the product master,
/// which carries no tax rate - see the note in the README.
/// </param>
/// <param name="UnitId">The unit the quantity is in. Defaults to the product's stock unit.</param>
/// <param name="Discount">What comes off the line before tax.</param>
/// <param name="BatchId">The batch sold, where the product is batched.</param>
/// <param name="BatchNumber">The same batch by its number, for a caller that has not got its id.</param>
/// <param name="SerialNumbers">The units sold, where the product is serialised.</param>
public sealed record SalesInvoiceLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal Rate,
    decimal TaxPercentage = 0m,
    Guid? UnitId = null,
    decimal Discount = 0m,
    Guid? BatchId = null,
    string? BatchNumber = null,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>One charge carried beside the goods.</summary>
/// <param name="LedgerId">The account it posts to, from the firm's charge matrix.</param>
/// <param name="Amount">What it comes to. Always positive; the matrix decides the sign.</param>
public sealed record SalesInvoiceChargeInput(Guid LedgerId, decimal Amount);

/// <summary>Enters a sales invoice as a draft.</summary>
/// <param name="Date">The invoice date.</param>
/// <param name="CustomerLedgerId">The customer billed.</param>
/// <param name="WarehouseId">The warehouse the goods will leave.</param>
/// <param name="Lines">What is being sold.</param>
/// <param name="Charges">Freight, packing, a discount - whatever the document carries.</param>
/// <param name="Mode">The tax mode. Defaults from the firm's regime.</param>
/// <param name="ReferenceNumber">The customer's own reference.</param>
/// <param name="Narration">What is printed on the invoice.</param>
/// <param name="Kind">Whether goods are going out or coming back. A sale unless stated.</param>
/// <param name="ReturnsInvoiceId">
/// The invoice a return is against. Optional, which is the business's answer of
/// 2026-08-12: goods turn up without their paperwork, and a counter that could not record
/// them would be a counter that turns customers away. Naming it is what lets the credit
/// find the debt, and what lets the goods come back at the cost they left at.
/// </param>
/// <remarks>
/// A draft, and only a draft. Nothing moves until it is posted, which is a command of its
/// own - so an invoice can be corrected while it is being typed, and a mistake found at
/// the counter costs a correction rather than a cancellation and a credit note.
/// </remarks>
public sealed record CreateSalesInvoiceCommand(
    DateOnly Date,
    Guid CustomerLedgerId,
    Guid WarehouseId,
    IReadOnlyList<SalesInvoiceLineInput> Lines,
    IReadOnlyList<SalesInvoiceChargeInput>? Charges = null,
    TaxMode? Mode = null,
    string? ReferenceNumber = null,
    string? Narration = null,
    SalesDocumentKind Kind = SalesDocumentKind.Invoice,
    Guid? ReturnsInvoiceId = null) : ICommand<SalesInvoiceResponse>, ITransactional;

/// <summary>An invoice as it now stands.</summary>
/// <param name="SalesInvoiceId">The invoice.</param>
/// <param name="Number">The number its series issued.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Taxable">The goods, net of line discounts and before tax.</param>
/// <param name="Tax">The tax on them.</param>
/// <param name="ChargeTotal">What the charges add, net of what they deduct.</param>
/// <param name="RoundingDifference">What rounding the total to the currency moved it by.</param>
/// <param name="Total">What the customer owes.</param>
public sealed record SalesInvoiceResponse(
    Guid SalesInvoiceId,
    string Number,
    SalesInvoiceStatus Status,
    decimal Taxable,
    decimal Tax,
    decimal ChargeTotal,
    decimal RoundingDifference,
    decimal Total);

/// <summary>Validates <see cref="CreateSalesInvoiceCommand"/>.</summary>
/// <remarks>
/// Shape only. Whether the customer is a customer, whether the warehouse belongs to this
/// firm and whether the goods exist are all questions for the aggregates that own them -
/// this catches the ones answerable without reading anything.
/// </remarks>
public sealed class CreateSalesInvoiceCommandValidator
    : AbstractValidator<CreateSalesInvoiceCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateSalesInvoiceCommandValidator"/> class.</summary>
    public CreateSalesInvoiceCommandValidator()
    {
        RuleFor(c => c.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("An invoice date is required.");

        RuleFor(c => c.CustomerLedgerId)
            .NotEqual(Guid.Empty)
            .WithMessage("An invoice must name the customer it is billed to.");

        RuleFor(c => c.WarehouseId)
            .NotEqual(Guid.Empty)
            .WithMessage("An invoice must name the warehouse its goods leave.");

        RuleFor(c => c.Mode!.Value).IsInEnum().When(c => c.Mode is not null);

        RuleFor(c => c.Lines)
            .NotEmpty()
            .WithMessage("An invoice needs at least one line.");

        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId)
                .NotEqual(Guid.Empty)
                .WithMessage("Each line must name a product.");

            line.RuleFor(l => l.Quantity)
                .GreaterThan(0m)
                .WithMessage("A sales line must be for a positive quantity.");

            line.RuleFor(l => l.Rate)
                .GreaterThanOrEqualTo(0m)
                .WithMessage("A rate cannot be negative.");

            line.RuleFor(l => l.Discount)
                .GreaterThanOrEqualTo(0m)
                .WithMessage("A discount cannot be negative.");

            line.RuleFor(l => l.TaxPercentage)
                .InclusiveBetween(0m, 100m)
                .WithMessage("A tax rate runs from 0 to 100 per cent.");
        });

        RuleForEach(c => c.Charges).ChildRules(charge =>
        {
            charge.RuleFor(c => c.LedgerId)
                .NotEqual(Guid.Empty)
                .WithMessage("Each charge must name the account it posts to.");

            charge.RuleFor(c => c.Amount)
                .GreaterThan(0m)
                .WithMessage(
                    "A charge is entered as a positive amount; whether it adds or deducts "
                    + "is decided by the charge itself.");
        });
    }
}
