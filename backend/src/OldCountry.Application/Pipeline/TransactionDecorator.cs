using System.Runtime.CompilerServices;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Pipeline;

/// <summary>
/// Runs a command in one database transaction, so all its writes commit or roll back together.
/// A command that starts while a transaction is open joins that transaction.
/// </summary>
internal static class TransactionDecorator
{
    internal sealed class CommandHandler<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, IAppDbContext db)
        : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
    {
        private static readonly bool Skip = typeof(TCommand).IsDefined(typeof(NoTransactionAttribute), inherit: false);

        internal ICommandHandler<TCommand, TResult> Inner => inner;

        public async Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)
        {
            if (Skip || db.Database.CurrentTransaction is not null)
                return await inner.Handle(command, cancellationToken);

            // Commit only on success. After a refusal (a union result that holds an IError) or an exception,
            // the transaction does not commit, and its disposal rolls back all partial writes.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await inner.Handle(command, cancellationToken);
            if (result is not IUnion { Value: IError })
                await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }
}
