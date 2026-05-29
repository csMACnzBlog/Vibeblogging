---
title: EF Core Change Tracking Performance Pitfalls
date: 2026-05-29
tags: dotnet, csharp, efcore, performance
image: ef-core-change-tracking-performance-pitfalls.png
---

EF Core's change tracker is one of those features you barely notice when things are small, and then suddenly *everything* feels slower once your app grows up.

I've been bitten by this a few times. The fixes usually weren't dramatic rewrites — just a better understanding of when tracking helps and when it quietly adds overhead.

Let's walk through the practical patterns.

## Use `AsNoTracking()` for read-only queries

If you're fetching data just to render a response, don't pay tracking costs you won't use.

```csharp
public sealed class OrdersService(AppDbContext db)
{
    public async Task<IReadOnlyList<OrderSummaryDto>> GetRecentAsync(CancellationToken ct)
    {
        return await db.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAtUtc >= DateTime.UtcNow.AddDays(-7))
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new OrderSummaryDto(o.Id, o.CustomerName, o.Total))
            .ToListAsync(ct);
    }
}
```

Tracking is great when you're about to update entities. For pure reads, it's usually unnecessary work.

## Watch out for accidental graph tracking

A common trap is loading a large object graph and then updating one field.

```csharp
var invoice = await db.Invoices
    .Include(x => x.Lines)
    .ThenInclude(x => x.Taxes)
    .SingleAsync(x => x.Id == id, ct);

invoice.Status = InvoiceStatus.Paid;
await db.SaveChangesAsync(ct);
```

This works, but now the tracker is watching everything you loaded. If all you need is a status update, use a focused write path instead.

## Prefer targeted updates when possible

If you don't need full entity materialization, update directly.

```csharp
var updated = await db.Invoices
    .Where(x => x.Id == id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(x => x.Status, InvoiceStatus.Paid)
        .SetProperty(x => x.PaidAtUtc, DateTime.UtcNow), ct);

if (updated == 0)
{
    return Results.NotFound();
}

return Results.NoContent();
```

`ExecuteUpdateAsync` avoids loading entities into the change tracker, which can make hot paths much cheaper.

## Be deliberate with `AutoDetectChangesEnabled`

In batch operations, EF repeatedly checking for changes can dominate runtime.

```csharp
public async Task ImportAsync(IEnumerable<ProductImportRow> rows, CancellationToken ct)
{
    db.ChangeTracker.AutoDetectChangesEnabled = false;

    try
    {
        foreach (var row in rows)
        {
            db.Products.Add(new Product
            {
                Sku = row.Sku,
                Name = row.Name,
                Price = row.Price
            });
        }

        await db.SaveChangesAsync(ct);
    }
    finally
    {
        db.ChangeTracker.AutoDetectChangesEnabled = true;
    }
}
```

Don't flip this switch globally. Use it in narrow, measured scenarios where you've confirmed it's a bottleneck.

## Measure tracker size during debugging

When a request feels weirdly slow, I like to quickly inspect tracker pressure.

```csharp
var trackedCount = db.ChangeTracker.Entries().Count();
logger.LogInformation("Tracked entities in scope: {TrackedCount}", trackedCount);
```

If that number is huge for a simple endpoint, you've likely found your culprit.

## A quick rule of thumb

I keep this mental model handy:

- **Read-only query?** Use `AsNoTracking()`
- **Single-field or set-based update?** Prefer `ExecuteUpdateAsync`
- **Large batch inserts/updates?** Measure with `AutoDetectChangesEnabled = false`
- **Unsure where time is going?** Check `ChangeTracker.Entries().Count()` and profile before changing code

EF Core's change tracker is genuinely useful — you don't need to avoid it. You just need to use it intentionally.

Once you do, performance tuning becomes much less mysterious.
