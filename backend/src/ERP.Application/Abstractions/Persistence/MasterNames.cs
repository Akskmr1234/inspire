using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Persistence;

/// <summary>The masters whose names are kept distinct within a firm.</summary>
public enum NamedMaster
{
    /// <summary>A product, by its description.</summary>
    Product = 1,

    /// <summary>
    /// A ledger of any kind - customer, supplier, employee, cash, bank or expense head -
    /// since every one of them is picked by name on the same vouchers.
    /// </summary>
    Ledger = 2,

    /// <summary>An account group.</summary>
    AccountGroup = 3,

    /// <summary>A product category.</summary>
    Category = 4,

    /// <summary>A brand.</summary>
    Brand = 5,

    /// <summary>A unit of measure.</summary>
    Unit = 6,

    /// <summary>A warehouse.</summary>
    Warehouse = 7,

    /// <summary>A tax rate.</summary>
    TaxSlab = 8,

    /// <summary>A country.</summary>
    Country = 9,

    /// <summary>A state, within its country.</summary>
    Region = 10,
}

/// <summary>
/// Says whether a master's name is already taken, so a second "Cash" or a second
/// product called the same as the first is refused by name rather than discovered on a
/// report that cannot tell the two apart.
/// </summary>
/// <remarks>
/// Checked when a master is created or renamed rather than enforced by an index: firms
/// that brought their books over from another system may already hold duplicates, and
/// an index would stop them from starting at all instead of stopping the next one.
/// Compared without regard to case or surrounding spaces.
/// </remarks>
public interface IMasterNames
{
    /// <summary>Says whether the name is used by another record of the same master.</summary>
    /// <param name="master">Which master.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="name">The name being given.</param>
    /// <param name="exceptId">The record being renamed, which may keep its own name.</param>
    /// <param name="scopeId">The country, for a state; otherwise unused.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when another record already has the name.</returns>
    Task<bool> IsTakenAsync(
        NamedMaster master,
        FirmId firmId,
        string name,
        Guid? exceptId = null,
        Guid? scopeId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The refusal every master gives for a name already in use.</summary>
public static class MasterNameErrors
{
    /// <summary>The name is already used by another record of the same master.</summary>
    /// <param name="master">Which master.</param>
    /// <param name="name">The name.</param>
    /// <returns>A conflict naming the clash.</returns>
    public static Error Taken(NamedMaster master, string name) =>
        Error.Conflict($"{master}.NameTaken", $"'{name.Trim()}' is already used. Choose another name.");
}

/// <summary>
/// A command that gives a master a name, which <see cref="MasterNameBehaviour{TRequest, TResponse}"/>
/// checks is not already taken before the handler runs.
/// </summary>
public interface INamesMaster
{
    /// <summary>Gets which master is being named.</summary>
    NamedMaster Master { get; }

    /// <summary>Gets the name being given.</summary>
    string NameGiven { get; }

    /// <summary>Gets the record being renamed, or null for a new one.</summary>
    Guid? RenamedId { get; }

    /// <summary>Gets the country, for a state; otherwise null.</summary>
    Guid? NameScopeId => null;
}

/// <summary>
/// Refuses a create or rename whose name another record of the same master already
/// has, before the handler is reached.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// A behaviour rather than a line in every handler: the masters are created and renamed
/// by a dozen commands, and the rule is the same for each of them.
/// </remarks>
public sealed class MasterNameBehaviour<TRequest, TResponse>
    : MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IMasterNames _names;
    private readonly Tenancy.ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="MasterNameBehaviour{TRequest, TResponse}"/> class.</summary>
    /// <param name="names">Answers whether a name is taken.</param>
    /// <param name="tenantContext">The caller's firm.</param>
    public MasterNameBehaviour(IMasterNames names, Tenancy.ITenantContext tenantContext)
    {
        _names = names;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        MediatR.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (request is INamesMaster named
            && _tenantContext.FirmId is { } firmId
            && !string.IsNullOrWhiteSpace(named.NameGiven)
            && await _names.IsTakenAsync(
                named.Master,
                firmId,
                named.NameGiven,
                named.RenamedId,
                named.NameScopeId,
                cancellationToken))
        {
            return ResultFactory.Failure<TResponse>(MasterNameErrors.Taken(named.Master, named.NameGiven));
        }

        return await next(cancellationToken);
    }
}
