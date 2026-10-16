using ERP.SharedKernel.Results;

namespace ERP.Application.Platform.Data;

/// <summary>How much of a firm's data a reset clears.</summary>
public enum DataResetScope
{
    /// <summary>
    /// Every transaction - invoices, returns, orders, stock documents, vouchers, bills,
    /// cheques, batches and serial numbers - with the stock and the books back to nothing
    /// and every number series back to its first number. The masters stay.
    /// </summary>
    Transactions = 1,

    /// <summary>
    /// The transactions and the masters the firm entered: customers, suppliers, products,
    /// categories, brands, units, warehouses and its own ledgers and groups. What setup
    /// created - the chart of accounts, the settings, the users and roles, the menu - stays,
    /// so the firm can start entering again at once.
    /// </summary>
    Everything = 2,
}

/// <summary>What a reset removed, by table.</summary>
/// <param name="Scope">What was cleared.</param>
/// <param name="Removed">Rows removed, by table, where any were.</param>
public sealed record DataResetResult(DataResetScope Scope, IReadOnlyDictionary<string, int> Removed);

/// <summary>What a reset would remove, counted before anybody confirms it.</summary>
/// <param name="FirmName">The firm, by the name a confirmation has to repeat.</param>
/// <param name="FirmCode">The firm's code, which a confirmation may repeat instead.</param>
/// <param name="Transactions">Documents by kind, all of which either reset removes.</param>
/// <param name="Masters">Masters by kind, which only clearing everything removes.</param>
public sealed record DataResetPreview(
    string FirmName,
    string FirmCode,
    IReadOnlyDictionary<string, int> Transactions,
    IReadOnlyDictionary<string, int> Masters);

/// <summary>Clears a firm's data, to hand it to a new customer or to test from nothing.</summary>
/// <remarks>
/// Only the selected firm, and all of it or none of it: one transaction, so a failure part
/// of the way leaves the firm exactly as it was. Not undoable, which is why the caller has
/// to name the firm it means.
/// </remarks>
public interface IDataReset
{
    /// <summary>Counts what a reset of the selected firm would remove.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The counts, or why they could not be read.</returns>
    Task<Result<DataResetPreview>> PreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Clears the selected firm's data.</summary>
    /// <param name="scope">What to clear.</param>
    /// <param name="confirmation">The firm's name or code, typed by whoever asked.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was removed, or why nothing was.</returns>
    Task<Result<DataResetResult>> ResetAsync(
        DataResetScope scope,
        string confirmation,
        CancellationToken cancellationToken = default);
}
