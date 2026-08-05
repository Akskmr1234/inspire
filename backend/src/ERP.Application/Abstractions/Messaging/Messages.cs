using ERP.SharedKernel.Results;
using MediatR;

namespace ERP.Application.Abstractions.Messaging;

/// <summary>A command that changes state and returns no value.</summary>
/// <remarks>
/// <para>
/// Feature code depends on these interfaces rather than on MediatR's directly.
/// MediatR is pinned at its last permissively-licensed release, so it appears in
/// the composition root and the behaviour pipeline but not throughout the
/// features - which keeps replacing it a mechanical change rather than a rewrite.
/// See <c>docs/adr/0002-third-party-licensing.md</c>.
/// </para>
/// <para>
/// Every command returns a <see cref="Result"/>. Expected failures - an unbalanced
/// voucher, a closed period, a duplicate code - are values, not exceptions, so a
/// caller cannot forget to handle them.
/// </para>
/// </remarks>
public interface ICommand : IRequest<Result>;

/// <summary>A command that changes state and returns a value.</summary>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>A query that reads state without changing it.</summary>
/// <typeparam name="TResponse">The value returned.</typeparam>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Handles a command that returns no value.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

/// <summary>Handles a command that returns a value.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The value produced.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

/// <summary>Handles a query.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResponse">The value returned.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;

/// <summary>
/// Marks a request whose handler must run inside a database transaction.
/// </summary>
/// <remarks>
/// Opt-in rather than applied to every command. A voucher posting touches the
/// voucher, its lines, and the numbering series, and a partial save would either
/// produce an unbalanced document or burn a number without issuing it. A query, by
/// contrast, gains nothing from a transaction but pays for one.
/// </remarks>
public interface ITransactional;
