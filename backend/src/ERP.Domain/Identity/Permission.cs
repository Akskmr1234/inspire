using System.Globalization;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;

namespace ERP.Domain.Identity;

/// <summary>
/// The actions a permission can grant, as listed in the specification.
/// </summary>
public enum PermissionVerb
{
    /// <summary>Read a record or open a screen.</summary>
    View = 1,

    /// <summary>Create a new record.</summary>
    Create = 2,

    /// <summary>Change an existing record.</summary>
    Edit = 3,

    /// <summary>Delete a record.</summary>
    Delete = 4,

    /// <summary>Approve a document, advancing it through its workflow.</summary>
    Approve = 5,

    /// <summary>Print a document.</summary>
    Print = 6,

    /// <summary>Export data to Excel, PDF, or CSV.</summary>
    Export = 7,
}

/// <summary>
/// One grantable action, identified by module, resource, and verb.
/// </summary>
/// <remarks>
/// <para>
/// Permissions are rows, not constants. The specification requires them to be
/// configurable from the database, so a new one can be introduced and assigned
/// without a deployment. The catalogue is seeded with the permissions the shipped
/// screens check, and administrators may add more for anything they configure
/// themselves.
/// </para>
/// <para>
/// Deliberately <em>not</em> tenant-scoped. The catalogue describes what the
/// software can do, which is the same for everybody; who may do it is the
/// tenant-specific part, and that lives on the role-permission assignment.
/// </para>
/// </remarks>
public sealed class Permission : Entity<PermissionId>
{
    private Permission(
        PermissionId id,
        string module,
        string resource,
        PermissionVerb verb,
        string description)
        : base(id)
    {
        Module = module;
        Resource = resource;
        Verb = verb;
        Description = description;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Permission()
    {
        Module = string.Empty;
        Resource = string.Empty;
        Description = string.Empty;
        Code = string.Empty;
    }

    /// <summary>Gets the owning module, for example <c>accounting</c>.</summary>
    public string Module { get; private set; }

    /// <summary>Gets the resource acted upon, for example <c>voucher</c>.</summary>
    public string Resource { get; private set; }

    /// <summary>Gets the action granted.</summary>
    public PermissionVerb Verb { get; private set; }

    /// <summary>
    /// Gets the canonical string form, <c>module:resource:verb</c>, for example
    /// <c>accounting:voucher:approve</c>.
    /// </summary>
    /// <remarks>
    /// Persisted rather than computed on read so it can carry a unique index and
    /// be matched with a single indexed lookup. It is what authorisation policies
    /// name and what appears in a token.
    /// </remarks>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Gets the human-readable description shown on the permissions screen.</summary>
    public string Description { get; private set; }

    /// <summary>Builds the canonical code for a module, resource, and verb.</summary>
    /// <param name="module">The module.</param>
    /// <param name="resource">The resource.</param>
    /// <param name="verb">The verb.</param>
    /// <returns>The lower-case <c>module:resource:verb</c> code.</returns>
    public static string BuildCode(string module, string resource, PermissionVerb verb) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{module.Trim().ToLowerInvariant()}:{resource.Trim().ToLowerInvariant()}:{verb.ToString().ToLowerInvariant()}");

    /// <summary>Creates a permission.</summary>
    /// <param name="module">The owning module.</param>
    /// <param name="resource">The resource acted upon.</param>
    /// <param name="verb">The action granted.</param>
    /// <param name="description">A human-readable description.</param>
    /// <returns>The permission, or a validation failure.</returns>
    public static Result<Permission> Create(
        string module,
        string resource,
        PermissionVerb verb,
        string description)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            return Result.Failure<Permission>(Error.Validation(
                "Permission.ModuleRequired", "A module is required."));
        }

        if (string.IsNullOrWhiteSpace(resource))
        {
            return Result.Failure<Permission>(Error.Validation(
                "Permission.ResourceRequired", "A resource is required."));
        }

        if (!Enum.IsDefined(verb))
        {
            return Result.Failure<Permission>(Error.Validation(
                "Permission.UnknownVerb", $"'{verb}' is not a recognised permission verb."));
        }

        Permission permission = new(
            PermissionId.NewId(),
            module.Trim().ToLowerInvariant(),
            resource.Trim().ToLowerInvariant(),
            verb,
            description?.Trim() ?? string.Empty)
        {
            Code = BuildCode(module, resource, verb),
        };

        return Result.Success(permission);
    }

    /// <inheritdoc />
    public override string ToString() => Code;
}
