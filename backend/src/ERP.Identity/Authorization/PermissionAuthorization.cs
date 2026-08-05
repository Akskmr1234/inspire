using System.Security.Claims;
using ERP.Application.Abstractions.Security;
using ERP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ERP.Identity.Authorization;

/// <summary>
/// Requires the caller to hold a named permission.
/// </summary>
/// <param name="PermissionCode">The canonical <c>module:resource:verb</c> code.</param>
public sealed record PermissionRequirement(string PermissionCode) : IAuthorizationRequirement;

/// <summary>
/// Declares that an endpoint requires a permission.
/// </summary>
/// <remarks>
/// <para>
/// Reads at the call site as <c>[RequiresPermission("accounting", "voucher",
/// PermissionVerb.Approve)]</c>, which is checked against the database at request
/// time. The attribute names the permission; it does not decide who holds it, so
/// changing who may approve a voucher remains a configuration change rather than
/// a deployment.
/// </para>
/// <para>
/// Policies are produced on demand by <see cref="PermissionPolicyProvider"/>,
/// so no list of policy names has to be registered at startup and kept in step
/// with the permission catalogue.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute : AuthorizeAttribute
{
    /// <summary>The prefix marking a policy name as a permission requirement.</summary>
    public const string PolicyPrefix = "perm:";

    /// <summary>
    /// Initialises a new instance of the <see cref="RequiresPermissionAttribute"/> class.
    /// </summary>
    /// <param name="module">The owning module, for example <c>accounting</c>.</param>
    /// <param name="resource">The resource acted upon, for example <c>voucher</c>.</param>
    /// <param name="verb">The action required.</param>
    public RequiresPermissionAttribute(string module, string resource, string verb)
        : base($"{PolicyPrefix}{module.ToLowerInvariant()}:{resource.ToLowerInvariant()}:{verb.ToLowerInvariant()}")
    {
        Module = module;
        Resource = resource;
        Verb = verb;
    }

    /// <summary>Gets the owning module.</summary>
    public string Module { get; }

    /// <summary>Gets the resource acted upon.</summary>
    public string Resource { get; }

    /// <summary>Gets the action required.</summary>
    public string Verb { get; }
}

/// <summary>
/// Manufactures an authorization policy for any permission code encountered.
/// </summary>
/// <remarks>
/// Registering one named policy per permission at startup would mean a startup
/// list that must be kept in step with a catalogue the customer can extend at
/// run time - and a missing entry throws rather than denying, so the failure
/// would be a 500 on a working endpoint. Generating policies on demand removes
/// that class of mistake entirely.
/// </remarks>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    /// <summary>
    /// Initialises a new instance of the <see cref="PermissionPolicyProvider"/> class.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) =>
        _fallback = new DefaultAuthorizationPolicyProvider(options);

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName is null
            || !policyName.StartsWith(RequiresPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return _fallback.GetPolicyAsync(policyName!);
        }

        string code = policyName[RequiresPermissionAttribute.PolicyPrefix.Length..];

        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(code))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

/// <summary>Checks a <see cref="PermissionRequirement"/> against the database.</summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionChecker _permissionChecker;

    /// <summary>
    /// Initialises a new instance of the <see cref="PermissionAuthorizationHandler"/> class.
    /// </summary>
    /// <param name="permissionChecker">The permission checker.</param>
    public PermissionAuthorizationHandler(IPermissionChecker permissionChecker) =>
        _permissionChecker = permissionChecker;

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        string? subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out Guid userId))
        {
            // No usable subject means no decision can be made. Leaving the context
            // unsucceeded denies by default, which is the only safe outcome.
            return;
        }

        bool granted = await _permissionChecker.HasPermissionAsync(
            UserId.From(userId), requirement.PermissionCode);

        if (granted)
        {
            context.Succeed(requirement);
        }
    }
}
