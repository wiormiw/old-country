using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OldCountry.Api.Common.Events;
using OldCountry.Api.Common.Realtime;
using OldCountry.Domain.Common;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Persistence.Interceptors;

/// <summary>
/// Notices what each save changes and hands it on once it has committed, never before: the
/// rows touched become refetch hints for the apps (<see cref="ChangeFeed"/>), and the domain
/// events entities raised go to their handlers (<see cref="DomainEventFeed"/>). Never before: an app told too early would refetch the old data, and
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
internal sealed class ChangeNotificationInterceptor(ChangeFeed feed, DomainEventFeed events) : ISaveChangesInterceptor, IDbTransactionInterceptor
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
        public List<IDomainEvent> SavingEvents = [];

        /// <summary>Saved inside the open transaction, waiting for it to commit.</summary>
        public LibraryChanges Uncommitted = LibraryChanges.None;
        public List<IDomainEvent> UncommittedEvents = [];

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
        var pending = _pending.GetOrCreateValue(context);
        pending.Saving = Describe(context.ChangeTracker);
        pending.SavingEvents = TakeEvents(context.ChangeTracker);
    }

    private void Saved(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        var saved = pending.Saving;
        var raised = pending.SavingEvents;
        pending.Saving = LibraryChanges.None;
        pending.SavingEvents = [];
        if (context.Database.CurrentTransaction is { } transaction && transaction.TransactionId == pending.Transaction)
        {
            pending.Uncommitted = pending.Uncommitted.Merge(saved);
            pending.UncommittedEvents.AddRange(raised);
        }
        else
        {
            // Committed by the save itself.
            feed.Publish(saved);
            events.Publish(raised);
        }
    }

    private void Forget(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        pending.Saving = LibraryChanges.None;
        pending.SavingEvents = [];
    }

    private void Started(TransactionEndEventData eventData)
    {
        if (eventData.Context is null) return;
        // A new transaction means any earlier one is over; changes it never committed are void.
        var pending = _pending.GetOrCreateValue(eventData.Context);
        pending.Transaction = eventData.TransactionId;
        pending.Uncommitted = LibraryChanges.None;
        pending.UncommittedEvents = [];
    }

    private void Committed(TransactionEndEventData eventData)
    {
        if (eventData.Context is null || !_pending.TryGetValue(eventData.Context, out var pending)) return;
        if (pending.Transaction != eventData.TransactionId) return;
        feed.Publish(pending.Uncommitted);
        events.Publish(pending.UncommittedEvents);
        pending.Uncommitted = LibraryChanges.None;
        pending.UncommittedEvents = [];
        pending.Transaction = null;
    }

    private void Abandon(TransactionEventData eventData)
    {
        if (eventData.Context is null || !_pending.TryGetValue(eventData.Context, out var pending)) return;
        if (pending.Transaction != eventData.TransactionId) return;
        pending.Uncommitted = LibraryChanges.None;
        pending.UncommittedEvents = [];
        pending.Transaction = null;
    }

    /// <summary>The domain events raised by entities in this save, taken off them so they go out once.</summary>
    internal static List<IDomainEvent> TakeEvents(ChangeTracker tracker)
    {
        List<IDomainEvent> raised = [];
        foreach (var entry in tracker.Entries<IHasDomainEvents>())
        {
            if (entry.Entity.DomainEvents.Count == 0) continue;
            raised.AddRange(entry.Entity.DomainEvents);
            entry.Entity.ClearDomainEvents();
        }
        return raised;
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
