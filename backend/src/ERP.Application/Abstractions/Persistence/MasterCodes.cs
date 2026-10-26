using System.Globalization;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Numbering;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Persistence;

/// <summary>The masters that number their own records, as their sequences are named.</summary>
public static class CodedMaster
{
    /// <summary>Products.</summary>
    public const string Product = "product";

    /// <summary>Customers.</summary>
    public const string Customer = "customer";

    /// <summary>Suppliers.</summary>
    public const string Supplier = "supplier";

    /// <summary>Employees.</summary>
    public const string Employee = "employee";

    /// <summary>Every other ledger: cash, bank, expense heads.</summary>
    public const string Ledger = "ledger";

    /// <summary>Account groups.</summary>
    public const string AccountGroup = "account-group";

    /// <summary>Product categories.</summary>
    public const string Category = "category";

    /// <summary>Brands.</summary>
    public const string Brand = "brand";

    /// <summary>Units of measure.</summary>
    public const string Unit = "unit";

    /// <summary>Warehouses.</summary>
    public const string Warehouse = "warehouse";

    /// <summary>Tax rates.</summary>
    public const string TaxSlab = "tax-slab";
}

/// <summary>Reads and writes the masters' code sequences.</summary>
public interface IMasterCodeSequenceRepository
{
    /// <summary>Finds a master's sequence, including one added in this unit of work and not yet saved.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="master">The master, from <see cref="CodedMaster"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The sequence, or null before the master's first automatic code.</returns>
    Task<MasterCodeSequence?> FindAsync(
        FirmId firmId,
        string master,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new sequence.</summary>
    /// <param name="sequence">The sequence.</param>
    void Add(MasterCodeSequence sequence);
}

/// <summary>Issues the code of a record entered without one.</summary>
public interface IMasterCodes
{
    /// <summary>
    /// The next code in the master's sequence - 10000, then 10001 and on - passing over
    /// any number a record already holds, such as one somebody typed in by hand.
    /// </summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="master">The master, from <see cref="CodedMaster"/>.</param>
    /// <param name="inUse">Whether a record of the master already holds a code.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The code, recorded as issued; saved with the record it is for.</returns>
    Task<string> NextAsync(
        FirmId firmId,
        string master,
        Func<string, Task<bool>> inUse,
        CancellationToken cancellationToken = default);
}

/// <summary>Issues codes from each master's own sequence.</summary>
/// <remarks>
/// The sequence is saved in the same unit of work as the record, so a record that fails
/// to save takes no number with it. Two records created at the same moment cannot both
/// take one number: the second save finds the sequence changed under it and is refused
/// rather than duplicated, and every master's unique code index stands behind that.
/// </remarks>
public sealed class MasterCodes : IMasterCodes
{
    private readonly IMasterCodeSequenceRepository _sequences;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="MasterCodes"/> class.</summary>
    /// <param name="sequences">The sequences.</param>
    /// <param name="tenantContext">The tenant a new sequence belongs to.</param>
    public MasterCodes(IMasterCodeSequenceRepository sequences, ITenantContext tenantContext)
    {
        _sequences = sequences;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<string> NextAsync(
        FirmId firmId,
        string master,
        Func<string, Task<bool>> inUse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        MasterCodeSequence? sequence = await _sequences.FindAsync(firmId, master, cancellationToken);

        if (sequence is null)
        {
            sequence = MasterCodeSequence.Start(_tenantContext.TenantId, firmId, master);
            _sequences.Add(sequence);
        }

        long number = sequence.Next;

        while (await inUse(Format(number)))
        {
            number++;
        }

        sequence.Issue(number);

        return Format(number);
    }

    private static string Format(long number) => number.ToString(CultureInfo.InvariantCulture);
}
