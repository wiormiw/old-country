using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OldCountry.Domain.Common;

namespace OldCountry.Api.Persistence.Interceptors;

/// <summary>
/// Stamps <c>CreatedAt</c>/<c>UpdatedAt</c> from <see cref="TimeProvider"/> on save. The domain
/// exposes them read-only, so they are written through EF's property entries.
/// </summary>
internal sealed class AuditableEntityInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    private const string CreatedAt = nameof(AuditableEntity<>.CreatedAt);
    private const string UpdatedAt = nameof(AuditableEntity<>.UpdatedAt);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) Stamp(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context) Stamp(context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext context)
    {
        // Postgres keeps microseconds; .NET has 100 ns ticks. Rounding here means the value a
        // response reports right after saving is the same one every later read returns.
        var now = clock.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % (TimeSpan.TicksPerMillisecond / 1000)));
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Metadata.FindProperty(CreatedAt) is null) continue;
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(CreatedAt).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(UpdatedAt).CurrentValue = now;
                    break;
            }
        }
    }
}
