---
title: EF Core Interceptors for Cross-Cutting Rules
date: 2026-05-14
tags: dotnet, efcore, architecture, data
image: ef-core-interceptors-cross-cutting-rules.png
---

Every team seems to eventually accumulate a little pile of EF Core code that isn't really domain logic, but still has to happen somewhere. Audit timestamps. Soft delete tweaks. Query logging. Maybe a bit of SQL tagging for diagnostics.

You can jam that stuff into repositories, or a giant `DbContext.SaveChangesAsync()` override, or a service that everyone promises they'll remember to call. I've done all three. None of them made me particularly proud.

That's where interceptors are handy. They let you plug cross-cutting behaviour into EF Core's pipeline without sprinkling the same rules through the rest of the app.

## The Smell: SaveChanges Logic That Keeps Growing

Here's the sort of override that starts reasonable and then slowly turns into a junk drawer:

```csharp
public sealed class AppDbContext : DbContext
{
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = utcNow;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = utcNow;
            }
        }

        return await base.SaveChangesAsync(ct);
    }
}
```

This works. Until it doesn't.

Once you want soft deletes as well, or multiple `DbContext` types, or some reusable package-level behaviour, that override starts pulling in too much unrelated responsibility.

## A Better Fit: SaveChangesInterceptor

A `SaveChangesInterceptor` gives you the same hook point, but as a separate class:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }
    DateTime UpdatedAtUtc { get; set; }
}

public sealed class AuditFieldsInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void UpdateAuditFields(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        var utcNow = DateTime.UtcNow;

        foreach (var entry in dbContext.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = utcNow;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = utcNow;
            }
        }
    }
}
```

Same behaviour, better home.

I like this because the `DbContext` goes back to describing your data model, and the auditing rule becomes a thing you can register, test, and reason about on its own.

## Registering the Interceptor

Once you've got the interceptor, wire it into EF Core when configuring the context:

```csharp
builder.Services.AddSingleton<AuditFieldsInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<AuditFieldsInterceptor>());
});
```

That's it. Every save for that context now passes through the interceptor.

If you have multiple interceptors, you can register them together:

```csharp
options.AddInterceptors(
    sp.GetRequiredService<AuditFieldsInterceptor>(),
    sp.GetRequiredService<SoftDeleteInterceptor>(),
    sp.GetRequiredService<SlowQueryLoggingInterceptor>());
```

That's a nice little clue that you're applying infrastructure policies, not baking behaviour into one random repository method.

## Soft Delete Is Another Good Candidate

Soft delete rules are another classic cross-cutting concern. Instead of letting `EntityState.Deleted` become a real delete, you can flip it into an update:

```csharp
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
}

public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplySoftDelete(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private static void ApplySoftDelete(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        foreach (var entry in dbContext.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAtUtc = DateTime.UtcNow;
        }
    }
}
```

Pair that with a global query filter and you've got soft delete behaviour applied consistently, without every handler remembering the special dance.

## Interceptors Aren't Just for SaveChanges

This is the bit I think people miss.

EF Core also has interceptors for database commands, connections, and transactions. So if you want to log slow SQL or tag commands with request metadata, you can do that closer to the actual database boundary.

Here's a compact `DbCommandInterceptor` that logs slow queries:

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

public sealed class SlowQueryLoggingInterceptor : DbCommandInterceptor
{
    private readonly ILogger<SlowQueryLoggingInterceptor> _logger;

    public SlowQueryLoggingInterceptor(ILogger<SlowQueryLoggingInterceptor> logger)
    {
        _logger = logger;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Duration > TimeSpan.FromMilliseconds(500))
        {
            _logger.LogWarning(
                "Slow SQL ({DurationMs}ms): {CommandText}",
                eventData.Duration.TotalMilliseconds,
                command.CommandText);
        }

        return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }
}
```

Now your slow-query logging sits in one place instead of being recreated in ad hoc diagnostics code all over the application.

## A Few Practical Warnings

Interceptors are great, but they're still infrastructure code, so I'd keep a few caveats in mind:

- **Keep them predictable.** Hidden side effects are only helpful when they're boring and consistent.
- **Don't put business decisions in them.** Setting audit fields? Great. Deciding whether an order may ship? Absolutely not.
- **Watch for duplication.** If you keep a `SaveChanges` override and add interceptors, it's easy to run the same rule twice.
- **Test them explicitly.** This is exactly the sort of code that feels invisible right up until it breaks production data.

They're powerful, but I'd argue the sweet spot is cross-cutting persistence behaviour, not cleverness for its own sake.

## Wrapping Up

If your `DbContext` is becoming the place where every persistence-related concern goes to hide, EF Core interceptors are worth a look. They give those rules a proper home and make the rest of the codebase a bit less magical in the bad way.

That's usually a trade I'll take.
