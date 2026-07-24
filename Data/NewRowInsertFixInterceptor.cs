using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Biktal.WebMVC.Data;

/// <summary>
/// Works around an EF Core tracking quirk in the prebuilt domain services: when a brand-new child
/// entity that already has a client-assigned (store-generated-by-convention) GUID key is attached to
/// an already-tracked parent via a navigation collection (e.g. <c>plan.Payments.Add(payment)</c>),
/// EF marks it as <see cref="EntityState.Modified"/> instead of <see cref="EntityState.Added"/> and
/// emits an UPDATE that affects 0 rows, throwing <see cref="DbUpdateConcurrencyException"/>.
///
/// Before each save we flip any <see cref="EntityState.Modified"/> entry whose row does not exist in
/// the database back to <see cref="EntityState.Added"/> so it is inserted correctly.
/// </summary>
public sealed class NewRowInsertFixInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        FixMisTrackedInserts(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await FixMisTrackedInsertsAsync(eventData.Context, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void FixMisTrackedInserts(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in ModifiedEntries(context))
        {
            if (entry.GetDatabaseValues() is null)
                entry.State = EntityState.Added;
        }
    }

    private static async Task FixMisTrackedInsertsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
            return;

        foreach (var entry in ModifiedEntries(context))
        {
            if (await entry.GetDatabaseValuesAsync(cancellationToken) is null)
                entry.State = EntityState.Added;
        }
    }

    private static List<EntityEntry> ModifiedEntries(DbContext context) =>
        context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified)
            .ToList();
}
