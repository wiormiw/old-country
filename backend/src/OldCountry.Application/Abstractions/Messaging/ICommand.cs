namespace OldCountry.Application.Abstractions.Messaging;

/// <summary>A request that changes state. It runs in a transaction, unless it has <see cref="NoTransactionAttribute"/>.</summary>
public interface ICommand<TResult>;
