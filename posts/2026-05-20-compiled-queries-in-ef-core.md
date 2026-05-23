---
title: Compiled Queries in EF Core
date: 2026-05-20
tags: dotnet, efcore, performance, csharp
image: compiled-queries-in-ef-core.png
---

EF Core already caches query plans internally, so for many apps that's enough. But if you've got very hot paths called constantly, compiled queries can trim extra overhead.

They're especially useful in read-heavy endpoints where the exact same query shape executes thousands of times.

## The Baseline Query

```csharp
public Task<Customer?> GetByEmailAsync(string email, CancellationToken ct)
{
    return _db.Customers
        .AsNoTracking()
        .SingleOrDefaultAsync(c => c.Email == email, ct);
}
```

This is perfectly fine in most cases.

## Compiling the Query

With `EF.CompileAsyncQuery`, you precompile once and reuse:

```csharp
private static readonly Func<AppDbContext, string, IAsyncEnumerable<Customer>>
    CustomerByEmailQuery = EF.CompileAsyncQuery(
        (AppDbContext db, string email) =>
            db.Customers
                .AsNoTracking()
                .Where(c => c.Email == email)
                .Take(1));

public async Task<Customer?> GetByEmailAsync(string email, CancellationToken ct)
{
    await foreach (var customer in CustomerByEmailQuery(_db, email).WithCancellation(ct))
    {
        return customer;
    }

    return null;
}
```

The query expression is compiled once, then reused without re-processing the full expression tree each call.

## Good Candidates

Compiled queries are best when all of these are true:

- The query runs frequently (hot path)
- The query shape is stable
- You're already doing obvious optimisations (`AsNoTracking`, indexes, projected DTOs)

If a query runs occasionally, you probably won't notice a meaningful gain.

## Keep Them Close to Repositories

I keep compiled query delegates near the data access methods that use them. That keeps discoverability high and avoids random static utility classes full of disconnected delegates.

Also, keep parameter lists simple. Complex dynamic branching inside compiled queries quickly hurts readability.

## Wrapping Up

Compiled queries are a targeted optimisation, not a blanket rule. Use them where profiling shows heavy repetition and query translation overhead.

Start with clean SQL and indexing, then reach for compiled queries when the numbers justify it.
