using ERP.SharedKernel.Results;

namespace ERP.Application.Platform.Import;

// ---------------------------------------------------------------------------------------
// The export package: what EasyRetail's database holds, in the shape the export script
// writes and the SQL Server reader returns. Every property is named as the query column
// that fills it, so the two sources produce the same document.
// ---------------------------------------------------------------------------------------

/// <summary>The business the books belong to.</summary>
/// <param name="Name">The company name.</param>
/// <param name="Address">The address lines, joined.</param>
/// <param name="City">The city.</param>
/// <param name="Phone">The telephone number.</param>
/// <param name="Mobile">The mobile number.</param>
/// <param name="Email">The email address.</param>
/// <param name="TaxNumber">The tax registration.</param>
/// <param name="BookBegin">When the books in EasyRetail start, as yyyy-MM-dd.</param>
public sealed record ErCompany(
    string Name,
    string Address,
    string City,
    string Phone,
    string Mobile,
    string Email,
    string TaxNumber,
    string BookBegin);

/// <summary>An account group (EasyRetail's "schedule").</summary>
/// <param name="Id">EasyRetail's identifier.</param>
/// <param name="Name">Its name.</param>
/// <param name="UnderId">The group above it, or 0 for one of the four heads.</param>
public sealed record ErGroup(int Id, string Name, int UnderId);

/// <summary>A ledger with its balances. Debits are positive, credits negative.</summary>
/// <param name="Id">EasyRetail's identifier.</param>
/// <param name="Name">Its name.</param>
/// <param name="GroupId">The schedule it sits under.</param>
/// <param name="Opening">The opening balance entered in EasyRetail.</param>
/// <param name="Balance">The balance as at the export date: opening plus every posting.</param>
/// <param name="Postings">How many postings it has had.</param>
public sealed record ErLedger(
    int Id,
    string Name,
    int GroupId,
    decimal Opening,
    decimal Balance,
    int Postings);

/// <summary>A customer or supplier, the party behind a ledger.</summary>
/// <param name="Id">EasyRetail's identifier.</param>
/// <param name="LedgerId">The ledger that carries their account.</param>
/// <param name="Code">Their code in EasyRetail, where it keeps one.</param>
/// <param name="Name">Their name.</param>
/// <param name="Address1">The first address line.</param>
/// <param name="Address2">The second address line.</param>
/// <param name="Mobile">The mobile number.</param>
/// <param name="Phone">The telephone number.</param>
/// <param name="Email">The email address.</param>
/// <param name="TaxNumber">The tax registration.</param>
/// <param name="CreditLimit">The credit limit, or 0 for none.</param>
/// <param name="CreditDays">The credit period in days, or 0 for none.</param>
/// <param name="City">The town or city.</param>
/// <param name="State">The state, as EasyRetail spells it.</param>
/// <param name="Country">The country, by name.</param>
/// <param name="PinCode">The postal or PIN code.</param>
/// <param name="Website">The website.</param>
/// <param name="Fax">The fax number.</param>
/// <param name="ContactPerson">The person to ask for.</param>
/// <param name="Tin">The taxpayer identification number.</param>
/// <param name="GstId">The GST identification number.</param>
/// <param name="CstNumber">The CST registration number.</param>
/// <param name="Area">The area, by name.</param>
/// <param name="Route">The route, by name.</param>
/// <param name="IsSms">1 where the party is sent text messages.</param>
/// <remarks>
/// The details after the credit period were added later, and default to nothing, so an
/// export written before them still reads.
/// </remarks>
public sealed record ErParty(
    int Id,
    int LedgerId,
    string Code,
    string Name,
    string Address1,
    string Address2,
    string Mobile,
    string Phone,
    string Email,
    string TaxNumber,
    decimal CreditLimit,
    int CreditDays,
    string City = "",
    string State = "",
    string Country = "",
    string PinCode = "",
    string Website = "",
    string Fax = "",
    string ContactPerson = "",
    string Tin = "",
    string GstId = "",
    string CstNumber = "",
    string Area = "",
    string Route = "",
    int IsSms = 0);

/// <summary>An employee, the person behind a ledger.</summary>
/// <param name="Id">EasyRetail's identifier.</param>
/// <param name="LedgerId">The ledger that carries their account.</param>
/// <param name="Name">Their name.</param>
/// <param name="Mobile">The mobile number.</param>
/// <param name="Email">The email address.</param>
public sealed record ErEmployee(int Id, int LedgerId, string Name, string Mobile, string Email);

/// <summary>A record with an identifier and a name: a sub-class, brand or warehouse.</summary>
/// <param name="Id">EasyRetail's identifier.</param>
/// <param name="Name">Its name.</param>
public sealed record ErNamed(int Id, string Name);

/// <summary>A product category.</summary>
/// <param name="Id">EasyRetail's identifier.</param>
/// <param name="Name">Its name.</param>
/// <param name="UnderId">The category above it, or 0 for the top.</param>
public sealed record ErCategory(int Id, string Name, int UnderId);

/// <summary>A unit of measure.</summary>
/// <param name="Id">EasyRetail's identifier.</param>
/// <param name="Name">Its name.</param>
/// <param name="ShortName">Its short form.</param>
/// <param name="BaseId">The unit it converts to, or 0 for a base unit.</param>
/// <param name="Factor">How many base units one of it holds.</param>
public sealed record ErUnit(int Id, string Name, string ShortName, int BaseId, decimal Factor);

/// <summary>A product.</summary>
/// <param name="Id">EasyRetail's identifier.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its description.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="CategoryId">Its category.</param>
/// <param name="SubClassId">Its sub-class.</param>
/// <param name="BrandId">Its brand.</param>
/// <param name="UnitId">The unit stock is kept in.</param>
/// <param name="PurchaseUnitId">The unit it is bought in.</param>
/// <param name="SalesUnitId">The unit it is sold in.</param>
/// <param name="ItemType">EasyRetail's item group. Kept for reference: every product is stocked.</param>
/// <param name="Cost">The purchase rate.</param>
/// <param name="Retail">The retail rate.</param>
/// <param name="Wholesale">The wholesale rate.</param>
/// <param name="Other">The other sales rate.</param>
/// <param name="Mrp">The maximum retail price.</param>
/// <param name="ProfitPercent">The profit percentage.</param>
/// <param name="MinLevel">The minimum stock level.</param>
/// <param name="ReorderLevel">The reorder level.</param>
/// <param name="MaxLevel">The maximum stock level.</param>
/// <param name="Rack">Where it is kept: the rack.</param>
/// <param name="Bin">Where it is kept: the bin.</param>
/// <param name="Upc">The barcode EasyRetail prints for it.</param>
/// <param name="Ean">Its EAN code.</param>
/// <param name="ShortDescription">The short description.</param>
/// <param name="Manufacturer">The manufacturer.</param>
/// <param name="Size">The size.</param>
/// <param name="Origin">The country of origin.</param>
/// <param name="Device">The device model.</param>
/// <param name="Colour">The colour.</param>
/// <param name="Battery">The battery.</param>
/// <param name="Ram">The memory.</param>
/// <param name="Storage">The storage.</param>
/// <param name="IsActive">Whether it is still sold.</param>
public sealed record ErProduct(
    int Id,
    string Code,
    string Name,
    string NameArabic,
    int CategoryId,
    int SubClassId,
    int BrandId,
    int UnitId,
    int PurchaseUnitId,
    int SalesUnitId,
    int ItemType,
    decimal Cost,
    decimal Retail,
    decimal Wholesale,
    decimal Other,
    decimal Mrp,
    decimal ProfitPercent,
    decimal MinLevel,
    decimal ReorderLevel,
    decimal MaxLevel,
    string Rack,
    string Bin,
    string Upc,
    string Ean,
    string ShortDescription,
    string Manufacturer,
    string Size,
    string Origin,
    string Device,
    string Colour,
    string Battery,
    string Ram,
    string Storage,
    bool IsActive);

/// <summary>A further barcode of a product, with the rates it scans at.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Barcode">The barcode.</param>
/// <param name="Cost">The cost.</param>
/// <param name="Retail">The retail rate.</param>
/// <param name="Wholesale">The wholesale rate.</param>
/// <param name="Mrp">The maximum retail price.</param>
public sealed record ErBarcode(
    int ProductId,
    string Barcode,
    decimal Cost,
    decimal Retail,
    decimal Wholesale,
    decimal Mrp);

/// <summary>A quantity of a product held in a warehouse.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="WarehouseId">The warehouse (EasyRetail's "godown").</param>
/// <param name="Batch">The batch, as EasyRetail names it.</param>
/// <param name="Quantity">How many are held. Negative where more was sold than recorded in.</param>
/// <param name="Cost">What one cost, for valuing it.</param>
public sealed record ErStock(int ProductId, int WarehouseId, string Batch, decimal Quantity, decimal Cost);

// ---------------------------------------------------------------------------------------
// The four steps of an import, each its own request so none runs long and the screen can
// show where it has got to. Every step can be run again: what was imported before is
// recognised by its EasyRetail identifier and left as it is.
// ---------------------------------------------------------------------------------------

/// <summary>The masters: groups, ledgers and parties, and the inventory masters.</summary>
/// <param name="Groups">The account groups.</param>
/// <param name="Ledgers">The ledgers.</param>
/// <param name="Customers">The customers.</param>
/// <param name="Suppliers">The suppliers.</param>
/// <param name="Employees">The employees.</param>
/// <param name="Categories">The product categories.</param>
/// <param name="Brands">The brands.</param>
/// <param name="Units">The units of measure.</param>
/// <param name="Warehouses">The warehouses.</param>
public sealed record ImportMastersRequest(
    IReadOnlyList<ErGroup> Groups,
    IReadOnlyList<ErLedger> Ledgers,
    IReadOnlyList<ErParty> Customers,
    IReadOnlyList<ErParty> Suppliers,
    IReadOnlyList<ErEmployee> Employees,
    IReadOnlyList<ErCategory> Categories,
    IReadOnlyList<ErNamed> Brands,
    IReadOnlyList<ErUnit> Units,
    IReadOnlyList<ErNamed> Warehouses);

/// <summary>A batch of products, with their barcodes and the sub-classes they name.</summary>
/// <param name="Products">The products.</param>
/// <param name="Barcodes">Their further barcodes.</param>
/// <param name="SubClasses">Every sub-class, filed as a category under each product's own.</param>
public sealed record ImportProductsRequest(
    IReadOnlyList<ErProduct> Products,
    IReadOnlyList<ErBarcode> Barcodes,
    IReadOnlyList<ErNamed> SubClasses);

/// <summary>The ledger balances to bring in as opening balances.</summary>
/// <param name="Ledgers">Every ledger, with its balance as at the export date.</param>
/// <param name="CarryProfitToEquity">
/// Whether income and expense balances are added together into one "profit and loss
/// brought forward" balance, as at a year end, rather than kept on their own ledgers.
/// </param>
public sealed record ImportBalancesRequest(
    IReadOnlyList<ErLedger> Ledgers,
    bool CarryProfitToEquity = true);

/// <summary>A batch of stock to bring in as opening stock.</summary>
/// <param name="AsAt">The date the opening stock is entered on.</param>
/// <param name="Rows">The quantities held.</param>
public sealed record ImportStockRequest(DateOnly AsAt, IReadOnlyList<ErStock> Rows);

/// <summary>What one step did.</summary>
/// <param name="Created">Records created in Inspire.</param>
/// <param name="Matched">Records matched to ones Inspire already had.</param>
/// <param name="AlreadyImported">Records brought across by an earlier run, left as they are.</param>
/// <param name="Skipped">Records left out because they are unused or cannot be imported.</param>
/// <param name="Warnings">What needs a person's attention, in words.</param>
public sealed record ImportStepResult(
    int Created,
    int Matched,
    int AlreadyImported,
    int Skipped,
    IReadOnlyList<string> Warnings);

/// <summary>What the balances step did.</summary>
/// <param name="Step">The counts.</param>
/// <param name="TotalDebit">The opening debit balances set.</param>
/// <param name="TotalCredit">The opening credit balances set.</param>
/// <param name="ProfitCarried">The income and expense balances carried to equity, debit positive.</param>
/// <param name="Difference">
/// The amount EasyRetail's own opening balances were out by, put on a ledger of its own so
/// the trial balance agrees. Debit positive.
/// </param>
public sealed record ImportBalancesResult(
    ImportStepResult Step,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal ProfitCarried,
    decimal Difference);

/// <summary>How to reach an EasyRetail database directly.</summary>
/// <param name="Server">The SQL Server, such as <c>192.168.1.10\SQLEXPRESS</c>.</param>
/// <param name="Database">The database, such as <c>EasyRetail2025</c>.</param>
/// <param name="UserName">A SQL login. Omit for Windows authentication.</param>
/// <param name="Password">Its password.</param>
/// <param name="AsAt">The date balances are read as at. Today when omitted.</param>
/// <param name="TrustServerCertificate">
/// Whether to accept the server's own certificate, which an EasyRetail installation
/// almost always has.
/// </param>
public sealed record EasyRetailConnection(
    string Server,
    string Database,
    string? UserName = null,
    string? Password = null,
    DateOnly? AsAt = null,
    bool TrustServerCertificate = true);

/// <summary>Reads an EasyRetail database, and writes the script that exports one.</summary>
public interface IEasyRetailSource
{
    /// <summary>Reads the database into the export document.</summary>
    /// <param name="connection">Where it is.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The document, as JSON, or why it could not be read.</returns>
    Task<Result<string>> ReadAsync(
        EasyRetailConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>The PowerShell script that writes the same document on the shop's own computer.</summary>
    /// <returns>The script.</returns>
    string ExportScript();
}

/// <summary>Brings an EasyRetail export into the selected firm.</summary>
public interface IEasyRetailImporter
{
    /// <summary>Imports the groups, ledgers, parties and inventory masters.</summary>
    /// <param name="request">The masters.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was done, or why nothing was.</returns>
    Task<Result<ImportStepResult>> ImportMastersAsync(
        ImportMastersRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Imports a batch of products.</summary>
    /// <param name="request">The products.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was done, or why nothing was.</returns>
    Task<Result<ImportStepResult>> ImportProductsAsync(
        ImportProductsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the ledgers' opening balances.</summary>
    /// <param name="request">The balances.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was done, or why nothing was.</returns>
    Task<Result<ImportBalancesResult>> ImportBalancesAsync(
        ImportBalancesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Enters a batch of opening stock.</summary>
    /// <param name="request">The stock.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was done, or why nothing was.</returns>
    Task<Result<ImportStepResult>> ImportStockAsync(
        ImportStockRequest request,
        CancellationToken cancellationToken = default);
}
