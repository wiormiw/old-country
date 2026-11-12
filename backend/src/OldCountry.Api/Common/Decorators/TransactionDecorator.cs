using System.Runtime.CompilerServices;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Common.Decorators;

/// <summary>
/// Runs a command in one database transaction, so its writes commit or roll back together.
/// A command sent while a transaction is already open joins it.
/// </summary>
internal static class TransactionDecorator
{
    internal sealed class CommandHandler<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, OldCountryDbContext db)
        : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
    {
        private static readonly bool Skip = typeof(TCommand).IsDefined(typeof(NoTransactionAttribute), inherit: false);

        internal ICommandHandler<TCommand, TResult> Inner => inner;

        public async Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)
        {
            if (Skip || db.Database.CurrentTransaction is not null)
                return await inner.Handle(command, cancellationToken);

            // Commit only on success. A refusal (a union result holding an IError) or an exception
            // leaves the transaction uncommitted, and disposing it rolls back any partial writes.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await inner.Handle(command, cancellationToken);
            if (result is not IUnion { Value: IError })
                await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }
}
