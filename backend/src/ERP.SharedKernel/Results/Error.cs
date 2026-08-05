namespace ERP.SharedKernel.Results;

/// <summary>
/// Classifies a failure so that transport layers can translate it without
/// inspecting the message. The API maps each kind onto an HTTP status code in
/// exactly one place.
/// </summary>
public enum ErrorKind
{
    /// <summary>No failure. Only ever carried by <see cref="Error.None"/>.</summary>
    None = 0,

    /// <summary>Input failed validation. Maps to HTTP 400.</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist. Maps to HTTP 404.</summary>
    NotFound = 2,

    /// <summary>
    /// The request contradicts the current state of the resource - a duplicate
    /// code, a concurrency clash, a document already approved. Maps to HTTP 409.
    /// </summary>
    Conflict = 3,

    /// <summary>
    /// The caller is authenticated but lacks the required permission.
    /// Maps to HTTP 403.
    /// </summary>
    Forbidden = 4,

    /// <summary>The caller is not authenticated. Maps to HTTP 401.</summary>
    Unauthorized = 5,

    /// <summary>
    /// A business rule or domain invariant was violated - an unbalanced voucher,
    /// a sale exceeding available batch stock. Maps to HTTP 422.
    /// </summary>
    BusinessRule = 6,

    /// <summary>An unexpected internal failure. Maps to HTTP 500.</summary>
    Unexpected = 7,
}

/// <summary>
/// A failure value. Errors are data, not exceptions: domain and application
/// code returns them, which keeps expected failure paths off the exception
/// mechanism and makes them impossible to forget to handle.
/// </summary>
/// <param name="Code">
/// A stable, machine-readable identifier in <c>Area.Condition</c> form, for
/// example <c>Voucher.NotBalanced</c>. Clients may branch on this; it must not
/// change once released. It is also the key used for translated messages.
/// </param>
/// <param name="Description">
/// A human-readable explanation in English, suitable for a developer or a log.
/// User-facing text is resolved on the client from <paramref name="Code"/> so
/// that it can be localised into English and Arabic.
/// </param>
/// <param name="Kind">How the failure should be classified by callers.</param>
public sealed record Error(string Code, string Description, ErrorKind Kind = ErrorKind.Validation)
{
    /// <summary>The absence of an error. Carried by every successful result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorKind.None);

    /// <summary>Creates a validation error (HTTP 400).</summary>
    /// <param name="code">Stable error code, e.g. <c>Product.CodeRequired</c>.</param>
    /// <param name="description">Developer-facing explanation.</param>
    /// <returns>The error.</returns>
    public static Error Validation(string code, string description) =>
        new(code, description, ErrorKind.Validation);

    /// <summary>Creates a not-found error (HTTP 404).</summary>
    /// <param name="code">Stable error code, e.g. <c>Ledger.NotFound</c>.</param>
    /// <param name="description">Developer-facing explanation.</param>
    /// <returns>The error.</returns>
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorKind.NotFound);

    /// <summary>Creates a conflict error (HTTP 409).</summary>
    /// <param name="code">Stable error code, e.g. <c>Product.DuplicateCode</c>.</param>
    /// <param name="description">Developer-facing explanation.</param>
    /// <returns>The error.</returns>
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorKind.Conflict);

    /// <summary>Creates a permission error (HTTP 403).</summary>
    /// <param name="code">Stable error code, e.g. <c>Voucher.ApproveDenied</c>.</param>
    /// <param name="description">Developer-facing explanation.</param>
    /// <returns>The error.</returns>
    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorKind.Forbidden);

    /// <summary>Creates an authentication error (HTTP 401).</summary>
    /// <param name="code">Stable error code.</param>
    /// <param name="description">Developer-facing explanation.</param>
    /// <returns>The error.</returns>
    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorKind.Unauthorized);

    /// <summary>
    /// Creates a business-rule violation (HTTP 422). Use this for broken domain
    /// invariants rather than <see cref="Validation"/>, which is for malformed
    /// input.
    /// </summary>
    /// <param name="code">Stable error code, e.g. <c>Voucher.NotBalanced</c>.</param>
    /// <param name="description">Developer-facing explanation.</param>
    /// <returns>The error.</returns>
    public static Error BusinessRule(string code, string description) =>
        new(code, description, ErrorKind.BusinessRule);

    /// <summary>Creates an unexpected internal failure (HTTP 500).</summary>
    /// <param name="code">Stable error code.</param>
    /// <param name="description">Developer-facing explanation.</param>
    /// <returns>The error.</returns>
    public static Error Unexpected(string code, string description) =>
        new(code, description, ErrorKind.Unexpected);

    /// <inheritdoc />
    public override string ToString() => $"{Code}: {Description}";
}
