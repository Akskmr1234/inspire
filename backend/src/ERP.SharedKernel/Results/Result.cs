using System.Diagnostics.CodeAnalysis;

namespace ERP.SharedKernel.Results;

/// <summary>
/// The outcome of an operation that can fail for expected reasons.
/// </summary>
/// <remarks>
/// <para>
/// Expected failures - a duplicate product code, an unbalanced voucher, a
/// missing ledger - are returned as values rather than thrown. Exceptions are
/// reserved for genuinely unexpected conditions (a dropped connection, a bug).
/// </para>
/// <para>
/// This matters in an accounting system: a caller cannot accidentally ignore a
/// failure, because reading <see cref="Result{TValue}.Value"/> on a failed
/// result throws. Forgetting to catch an exception, by contrast, is silent.
/// </para>
/// </remarks>
public class Result
{
    /// <summary>Initialises a new instance of the <see cref="Result"/> class.</summary>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="error">The failure, or <see cref="Error.None"/> on success.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the success flag and the error disagree - a successful result
    /// carrying an error, or a failed result carrying none. Such a result would
    /// be meaningless, so it is rejected at construction.
    /// </exception>
    protected Result(bool isSuccess, Error error)
    {
        switch (isSuccess)
        {
            case true when error != Error.None:
                throw new InvalidOperationException("A successful result cannot carry an error.");
            case false when error == Error.None:
                throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the failure, or <see cref="Error.None"/> when
    /// <see cref="IsSuccess"/> is <see langword="true"/>.
    /// </summary>
    public Error Error { get; }

    /// <summary>Creates a successful result.</summary>
    /// <returns>A successful result.</returns>
    public static Result Success() => new(true, Error.None);

    /// <summary>Creates a successful result carrying a value.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="value">The value produced by the operation.</param>
    /// <returns>A successful result.</returns>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The failure.</param>
    /// <returns>A failed result.</returns>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Creates a failed result of a value-bearing type.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="error">The failure.</param>
    /// <returns>A failed result.</returns>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    /// <summary>
    /// Returns the first failure among <paramref name="results"/>, or success if
    /// they all succeeded. Useful for validating several independent conditions
    /// and reporting the first that fails.
    /// </summary>
    /// <param name="results">The results to inspect, in priority order.</param>
    /// <returns>The first failure, or success.</returns>
    public static Result FirstFailureOrSuccess(params ReadOnlySpan<Result> results)
    {
        foreach (Result result in results)
        {
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Success();
    }
}

/// <summary>The outcome of an operation that produces a value and can fail.</summary>
/// <typeparam name="TValue">The type of value produced on success.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    /// <summary>Initialises a new instance of the <see cref="Result{TValue}"/> class.</summary>
    /// <param name="value">The value, or <see langword="default"/> on failure.</param>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="error">The failure, or <see cref="Error.None"/> on success.</param>
    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>Gets the value produced by a successful operation.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the result is a failure. This is deliberate: it converts
    /// "forgot to check the result" from a silent null into an immediate,
    /// loud failure at the point of the mistake.
    /// </exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read the value of a failed result. Error was '{Error}'.");

    /// <summary>
    /// Lifts a value into a successful result, or into a failure when it is
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="value">The value to lift.</param>
    /// <returns>
    /// A successful result, or a <see cref="ErrorKind.NotFound"/> failure when
    /// <paramref name="value"/> is <see langword="null"/>.
    /// </returns>
    public static implicit operator Result<TValue>(TValue? value) => value is not null
        ? Success(value)
        : Failure<TValue>(Error.NotFound(
            $"{typeof(TValue).Name}.NotFound",
            $"No {typeof(TValue).Name} was found."));

    /// <summary>
    /// Attempts to read the value, in the <c>TryGet</c> idiom, so callers can
    /// branch without risking the throwing <see cref="Value"/> accessor.
    /// </summary>
    /// <param name="value">The value when successful; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when the result is a success.</returns>
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = IsSuccess ? _value : default;
        return IsSuccess;
    }

    /// <summary>
    /// Transforms the value of a successful result, propagating a failure
    /// unchanged.
    /// </summary>
    /// <typeparam name="TNext">The transformed value type.</typeparam>
    /// <param name="map">The projection to apply on success.</param>
    /// <returns>The mapped result.</returns>
    public Result<TNext> Map<TNext>(Func<TValue, TNext> map) => IsSuccess
        ? Success(map(Value))
        : Failure<TNext>(Error);

    /// <summary>
    /// Chains an operation that itself returns a result, propagating a failure
    /// unchanged. Lets a sequence of fallible steps be composed without nesting
    /// success checks.
    /// </summary>
    /// <typeparam name="TNext">The next value type.</typeparam>
    /// <param name="bind">The continuation to run on success.</param>
    /// <returns>The chained result.</returns>
    public Result<TNext> Bind<TNext>(Func<TValue, Result<TNext>> bind) => IsSuccess
        ? bind(Value)
        : Failure<TNext>(Error);

    /// <summary>Collapses both branches into a single value.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="onSuccess">Applied when the result is a success.</param>
    /// <param name="onFailure">Applied when the result is a failure.</param>
    /// <returns>The value produced by whichever branch applies.</returns>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);
}
