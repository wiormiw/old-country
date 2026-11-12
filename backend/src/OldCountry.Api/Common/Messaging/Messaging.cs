namespace OldCountry.Api.Common.Messaging;

/// <summary>A request that changes state. Runs in a transaction unless marked <see cref="NoTransactionAttribute"/>.</summary>
public interface ICommand<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>A request that only reads.</summary>
public interface IQuery<TResult>;

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// The command must not hold a database transaction open, e.g. because it waits on a slow
/// external process (a yt-dlp download) between its reads and writes.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NoTransactionAttribute : Attribute;
