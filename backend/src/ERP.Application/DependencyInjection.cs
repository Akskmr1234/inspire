using System.Reflection;
using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ERP.Application;

/// <summary>Registers the application layer.</summary>
public static class DependencyInjection
{
    /// <summary>Adds the use-case handlers, validators, and pipeline behaviours.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Assembly assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            // Order matters and is the whole point of the pipeline. Logging wraps
            // everything so a failure is recorded whatever caused it; validation
            // runs before the transaction so a malformed request never opens one.
            configuration.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehaviour<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}

/// <summary>Runs any registered validators before the handler.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Turns validation failures into a <see cref="Result"/> rather than throwing.
/// The API maps a failed result onto ProblemDetails in one place, so a validation
/// error and a domain-rule failure reach the client in the same shape - which is
/// what the client needs in order to display either of them.
/// </remarks>
public sealed class ValidationBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>Initialises a new instance of the <see cref="ValidationBehaviour{TRequest, TResponse}"/> class.</summary>
    /// <param name="validators">The validators registered for this request.</param>
    public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators) =>
        _validators = validators;

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!_validators.Any())
        {
            return await next(cancellationToken);
        }

        List<string> failures = [];

        foreach (IValidator<TRequest> validator in _validators)
        {
            FluentValidation.Results.ValidationResult result =
                await validator.ValidateAsync(request, cancellationToken);

            failures.AddRange(result.Errors.Select(e => e.ErrorMessage));
        }

        if (failures.Count == 0)
        {
            return await next(cancellationToken);
        }

        // Every failure is reported, not just the first. A form with three bad
        // fields should light up three fields, not make the user resubmit twice.
        Error error = Error.Validation(
            $"{typeof(TRequest).Name}.Validation",
            string.Join(" ", failures));

        return ResultFactory.Failure<TResponse>(error);
    }
}

/// <summary>Logs each request, its outcome, and how long it took.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed partial class LoggingBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;

    /// <summary>Initialises a new instance of the <see cref="LoggingBehaviour{TRequest, TResponse}"/> class.</summary>
    /// <param name="logger">The logger.</param>
    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger) =>
        _logger = logger;

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        string name = typeof(TRequest).Name;
        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        TResponse response = await next(cancellationToken);

        TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);

        // A returned failure is logged as a warning, not an error: an unbalanced
        // voucher is a user mistake, not a fault in the system, and treating it as
        // an error trains everyone to ignore the error log.
        if (response is Result { IsFailure: true } failed)
        {
            LogRequestFailed(_logger, name, failed.Error.Code, elapsed.TotalMilliseconds);
        }
        else
        {
            LogRequestHandled(_logger, name, elapsed.TotalMilliseconds);
        }

        return response;
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Handled {Request} in {ElapsedMs} ms")]
    private static partial void LogRequestHandled(
        ILogger logger,
        string request,
        double elapsedMs);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "{Request} returned {ErrorCode} after {ElapsedMs} ms")]
    private static partial void LogRequestFailed(
        ILogger logger,
        string request,
        string errorCode,
        double elapsedMs);
}

/// <summary>Wraps handlers marked <see cref="ITransactional"/> in a transaction.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// <para>
/// Also rolls back on a returned <em>failure</em>, not only on an exception. That
/// matters: a handler that reserves a document number and then finds the voucher
/// unbalanced returns a failure, and without a rollback the number would be
/// consumed for a voucher that never existed - leaving a gap in the sequence that
/// an auditor will ask about.
/// </para>
/// </remarks>
public sealed class TransactionBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="TransactionBehaviour{TRequest, TResponse}"/> class.</summary>
    /// <param name="unitOfWork">The unit of work.</param>
    public TransactionBehaviour(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (request is not ITransactional)
        {
            return await next(cancellationToken);
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                TResponse response = await next(token);

                if (response is Result { IsFailure: true } failed)
                {
                    throw new TransactionRollbackException(failed, failed.Error.Code);
                }

                return response;
            },
            cancellationToken);
    }
}

/// <summary>
/// Signals that a handler returned a failure and its transaction must roll back.
/// </summary>
/// <remarks>
/// An exception is the only way to abort an ambient transaction, but the failure is
/// a value rather than a fault - so it is carried through and unwrapped rather than
/// surfacing as a 500.
/// </remarks>
public sealed class TransactionRollbackException : Exception
{
    /// <summary>Initialises a new instance of the <see cref="TransactionRollbackException"/> class.</summary>
    /// <param name="response">The failed response to return to the caller.</param>
    /// <param name="errorCode">The error code, for the message.</param>
    public TransactionRollbackException(object response, string errorCode)
        : base($"Rolling back: the handler returned {errorCode}.") => Response = response;

    /// <summary>Initialises a new instance of the <see cref="TransactionRollbackException"/> class.</summary>
    public TransactionRollbackException()
        : base("Rolling back: the handler returned a failure.") => Response = null!;

    /// <summary>Initialises a new instance of the <see cref="TransactionRollbackException"/> class.</summary>
    /// <param name="message">The message.</param>
    public TransactionRollbackException(string message)
        : base(message) => Response = null!;

    /// <summary>Initialises a new instance of the <see cref="TransactionRollbackException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TransactionRollbackException(string message, Exception innerException)
        : base(message, innerException) => Response = null!;

    /// <summary>Gets the failed response the caller should receive.</summary>
    public object Response { get; }
}

/// <summary>
/// Builds a failed <see cref="Result"/> or <see cref="Result{T}"/> when only the
/// closed generic type is known at run time.
/// </summary>
/// <remarks>
/// The pipeline is generic over <c>TResponse</c>, so it cannot name
/// <c>Result&lt;Something&gt;</c> statically. This resolves the right factory once
/// per closed type and caches it, so the reflection cost is paid at startup rather
/// than per request.
/// </remarks>
internal static class ResultFactory
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<Error, object>>
        Factories = new();

    internal static TResponse Failure<TResponse>(Error error)
    {
        Type responseType = typeof(TResponse);

        if (responseType == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        Func<Error, object> factory = Factories.GetOrAdd(responseType, static type =>
        {
            Type valueType = type.GetGenericArguments()[0];

            MethodInfo method = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
                .MakeGenericMethod(valueType);

            return error => method.Invoke(null, [error])!;
        });

        return (TResponse)factory(error);
    }
}
