using ERP.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>
/// Base controller supplying the single translation from a domain
/// <see cref="Error"/> to an RFC 9457 problem response.
/// </summary>
/// <remarks>
/// Every endpoint returns failures through <see cref="Problem(Error)"/>, so the
/// mapping from error kind to status code exists exactly once. Repeating it per
/// controller is how one endpoint ends up answering 400 for a condition another
/// answers 422 for, which clients then have to special-case.
/// </remarks>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Translates a domain error into a problem response.</summary>
    /// <param name="error">The failure.</param>
    /// <returns>The problem response.</returns>
    protected ObjectResult Problem(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        int status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,

            // 422 rather than 400: the request was well-formed and understood, but
            // a business rule refused it. A client can tell "you sent nonsense"
            // from "the books will not accept this" without parsing the message.
            ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,

            _ => StatusCodes.Status500InternalServerError,
        };

        // The stable error code goes in the title so clients branch on it rather
        // than on prose that may be reworded or translated.
        return Problem(
            detail: error.Description,
            statusCode: status,
            title: error.Code);
    }
}
