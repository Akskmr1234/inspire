using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Accounting;
using ERP.Domain.Purchase;
using FluentValidation;

namespace ERP.Application.Purchase;

/// <summary>One product on a purchase being entered.</summary>
/// <param name="ProductId">The product bought.</param>
/// <param name="Quantity">How many, in the unit named.</param>
/// <param name="Rate">What one of them cost.</param>
/// <param name="TaxPercentage">
/// The rate the supplier charged tax at. Supplied per line rather than read from the
/// product master, which carries no tax rate - see the note in the README.
/// </param>
/// <param name="UnitId">The unit the quantity is in. Defaults to the product's stock unit.</param>
/// <param name="Discount">What comes off the line before tax.</param>
/// <param name="BatchNumber">
/// The batch the goods arrived in, where the product is batched. A number rather than an
/// identifier, because a purchase is usually the moment the batch comes into existence.
/// </param>
/// <param name="ExpiresOn">When that batch expires, where the supplier stated it.</param>
/// <param name="SerialNumbers">The units arriving, where the product is serialised.</param>
public sealed record PurchaseInvoiceLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal Rate,
    decimal TaxPercentage = 0m,
    Guid? UnitId = null,
    decimal Discount = 0m,
    string? BatchNumber = null,
    DateOnly? ExpiresOn = null,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>One charge carried beside the goods.</summary>
/// <param name="LedgerId">The account it posts to, from the firm's charge matrix.</param>
/// <param name="Amount">What it comes to. Always positive; the matrix decides the sign.</param>
public sealed record PurchaseInvoiceChargeInput(Guid LedgerId, decimal Amount);

/// <summary>Enters a purchase as a draft.</summary>
/// <param name="Date">The date the firm books it on.</param>
/// <param name="SupplierLedgerId">The supplier billing.</param>
/// <param name="WarehouseId">The warehouse the goods arrive at.</param>
/// <param name="Lines">What is being bought.</param>
/// <param name="Charges">Freight, insurance, a discount - whatever the document carries.</param>
/// <param name="Mode">The tax mode. Defaults from the firm's regime.</param>
/// <param name="SupplierInvoiceNumber">The number printed on the supplier's invoice.</param>
/// <param name="SupplierInvoiceDate">The date printed on it.</param>
/// <param name="Narration">What is recorded against the entry.</param>
/// <param name="Kind">Whether goods are arriving or going back. A purchase unless stated.</param>
/// <param name="ReturnsInvoiceId">
/// The purchase a return is against. Optional, for the reason a sales return's is: goods
/// go back without the original paperwork to hand often enough that refusing would leave a
/// storekeeper unable to record what has just left the yard.
/// </param>
/// <remarks>
/// A draft, and only a draft. Nothing moves until it is posted, which is a command of its
/// own - so a purchase can be corrected while it is being keyed off the supplier's
/// document, and a mistyped rate costs a correction rather than a cancellation.
/// </remarks>
public sealed record CreatePurchaseInvoiceCommand(
    DateOnly Date,
    Guid SupplierLedgerId,
    Guid WarehouseId,
    IReadOnlyList<PurchaseInvoiceLineInput> Lines,
    IReadOnlyList<PurchaseInvoiceChargeInput>? Charges = null,
    TaxMode? Mode = null,
    string? SupplierInvoiceNumber = null,
    DateOnly? SupplierInvoiceDate = null,
    string? Narration = null,
    PurchaseDocumentKind Kind = PurchaseDocumentKind.Invoice,
    Guid? ReturnsInvoiceId = null) : ICommand<PurchaseInvoiceResponse>, ITransactional;

/// <summary>A purchase as it now stands.</summary>
/// <param name="PurchaseInvoiceId">The document.</param>
/// <param name="Number">The number its series issued.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Taxable">The goods, net of line discounts and before tax.</param>
/// <param name="Tax">The input tax on them.</param>
/// <param name="ChargeTotal">What the charges add, net of what they deduct.</param>
/// <param name="RoundingDifference">What rounding the total to the currency moved it by.</param>
/// <param name="Total">What the supplier is owed.</param>
public sealed record PurchaseInvoiceResponse(
    Guid PurchaseInvoiceId,
    string Number,
    PurchaseInvoiceStatus Status,
    decimal Taxable,
    decimal Tax,
    decimal ChargeTotal,
    decimal RoundingDifference,
    decimal Total);

/// <summary>Validates <see cref="CreatePurchaseInvoiceCommand"/>.</summary>
/// <remarks>
/// Shape only. Whether the supplier is a supplier, whether the warehouse belongs to this
/// firm and whether the goods exist are all questions for the aggregates that own them.
/// </remarks>
public sealed class CreatePurchaseInvoiceCommandValidator
    : AbstractValidator<CreatePurchaseInvoiceCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreatePurchaseInvoiceCommandValidator"/> class.</summary>
    public CreatePurchaseInvoiceCommandValidator()
    {
        RuleFor(c => c.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("A document date is required.");

        RuleFor(c => c.SupplierLedgerId)
            .NotEqual(Guid.Empty)
            .WithMessage("A purchase must name the supplier it is billed by.");

        RuleFor(c => c.WarehouseId)
            .NotEqual(Guid.Empty)
            .WithMessage("A purchase must name the warehouse its goods arrive at.");

        RuleFor(c => c.Mode!.Value).IsInEnum().When(c => c.Mode is not null);

        RuleFor(c => c.SupplierInvoiceNumber)
            .NotEmpty()
            .When(c => c.SupplierInvoiceDate is not null)
            .WithMessage("A supplier invoice date needs the number it belongs to.");

        RuleFor(c => c.Lines)
            .NotEmpty()
            .WithMessage("A purchase needs at least one line.");

        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId)
                .NotEqual(Guid.Empty)
                .WithMessage("Each line must name a product.");

            line.RuleFor(l => l.Quantity)
                .GreaterThan(0m)
                .WithMessage("A purchase line must be for a positive quantity.");

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
