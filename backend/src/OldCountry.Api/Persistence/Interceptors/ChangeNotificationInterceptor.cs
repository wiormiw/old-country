using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OldCountry.Api.Common.Realtime;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Persistence.Interceptors;

/// <summary>
/// Notices what each save changes and reports it to the apps (through <see cref="ChangeFeed"/>)
/// once it has committed, never before: an app told too early would refetch the old data, and
/// one told about a rolled-back change would refetch for nothing.
///
/// Being in the save pipeline means every writer is covered without remembering to notify:
/// API commands, the YouTube sync scheduler, the library scanner and the download worker.
///
/// <list type="bullet">
/// <item>A save outside a transaction commits by itself: reported right after it.</item>
/// <item>Saves inside a transaction gather until it commits; a rollback (or a transaction
/// abandoned without committing) drops what they gathered.</item>
/// </list>
/// </summary>
internal sealed class ChangeNotificationInterceptor(ChangeFeed feed) : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    /// <summary>
    /// What each context has saved but not yet committed, by transaction. Weakly keyed, so a
    /// context dropped mid-transaction takes its unreported changes with it.
    /// </summary>
    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();

    private sealed class Pending
    {
        /// <summary>Gathered from the change tracker before the save; reported or dropped after it.</summary>
        public LibraryChanges Saving = LibraryChanges.None;

        /// <summary>Saved inside the open transaction, waiting for it to commit.</summary>
        public LibraryChanges Uncommitted = LibraryChanges.None;

        public Guid? Transaction;
    }

    // --- Saves

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Gather(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Gather(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    // --- Transactions

    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Started(eventData);
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Started(eventData);
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Committed(eventData);

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Committed(eventData);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Abandon(eventData);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Abandon(eventData);
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => Abandon(eventData);

    public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Abandon(eventData);
        return Task.CompletedTask;
    }

    // --- The bookkeeping

    private void Gather(DbContext? context)
    {
        if (context is null) return;
        _pending.GetOrCreateValue(context).Saving = Describe(context.ChangeTracker);
    }

    private void Saved(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        var saved = pending.Saving;
        pending.Saving = LibraryChanges.None;
        if (context.Database.CurrentTransaction is { } transaction && transaction.TransactionId == pending.Transaction)
            pending.Uncommitted = pending.Uncommitted.Merge(saved);
        else
            feed.Publish(saved); // committed by the save itself
    }

    private void Forget(DbContext? context)
    {
        if (context is not null && _pending.TryGetValue(context, out var pending)) pending.Saving = LibraryChanges.None;
    }

    private void Started(TransactionEndEventData eventData)
    {
        if (eventData.Context is null) return;
        // A new transaction means any earlier one is over; changes it never committed are void.
        var pending = _pending.GetOrCreateValue(eventData.Context);
        pending.Transaction = eventData.TransactionId;
        pending.Uncommitted = LibraryChanges.None;
    }

    private void Committed(TransactionEndEventData eventData)
    {
        if (eventData.Context is null || !_pending.TryGetValue(eventData.Context, out var pending)) return;
        if (pending.Transaction != eventData.TransactionId) return;
        feed.Publish(pending.Uncommitted);
        pending.Uncommitted = LibraryChanges.None;
        pending.Transaction = null;
    }

    private void Abandon(TransactionEventData eventData)
    {
        if (eventData.Context is null || !_pending.TryGetValue(eventData.Context, out var pending)) return;
        if (pending.Transaction != eventData.TransactionId) return;
        pending.Uncommitted = LibraryChanges.None;
        pending.Transaction = null;
    }

    /// <summary>Which playlists, songs and downloads the pending save touches.</summary>
    internal static LibraryChanges Describe(ChangeTracker tracker)
    {
        List<Guid>? playlists = null;
        bool tracks = false, downloads = false;
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            switch (entry.Entity)
            {
                // A playlist's songs are part of its row (a JSON column), so any change to them,
                // their order or its sync state shows up as the playlist being modified.
                case Playlist playlist:
                    (playlists ??= []).Add(playlist.Id.Value);
                    break;
                case Track or Artist:
                    tracks = true;
                    break;
                case DownloadJob:
                    downloads = true;
                    break;
            }
        }
        return new LibraryChanges(playlists ?? [], tracks, downloads);
    }
}
