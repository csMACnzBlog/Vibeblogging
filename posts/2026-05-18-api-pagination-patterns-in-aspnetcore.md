---
title: API Pagination Patterns in ASP.NET Core
date: 2026-05-18
tags: aspnetcore, api, dotnet, csharp, performance
image: api-pagination-patterns-in-aspnetcore.png
---

Pagination feels straightforward until an endpoint gets real traffic. Suddenly queries are expensive, clients need stable ordering, and "just use skip/take" starts showing cracks.

Here are the pagination patterns I reach for most in ASP.NET Core APIs.

## Offset Pagination (Skip/Take)

Offset pagination is easy to understand and quick to implement:

```csharp
app.MapGet("/orders", async (
    int page,
    int pageSize,
    AppDbContext db,
    CancellationToken ct) =>
{
    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 100);

    var items = await db.Orders
        .OrderByDescending(x => x.CreatedAtUtc)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new OrderDto(x.Id, x.Total, x.CreatedAtUtc))
        .ToListAsync(ct);

    return Results.Ok(items);
});
```

It works well for admin views and moderate datasets.

## The Offset Trade-Offs

As page numbers grow, SQL still scans and skips rows. It also gets unstable when new rows are inserted between requests, which can cause duplicates or gaps across pages.

For public feeds or high-write tables, cursor pagination is usually safer.

## Cursor Pagination

Cursor pagination uses a stable sort key and asks for "items after this key":

```csharp
app.MapGet("/events", async (
    DateTimeOffset? cursor,
    int pageSize,
    AppDbContext db,
    CancellationToken ct) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);

    var query = db.Events
        .OrderByDescending(x => x.CreatedAtUtc)
        .ThenByDescending(x => x.Id)
        .AsQueryable();

    if (cursor is not null)
    {
        query = query.Where(x => x.CreatedAtUtc < cursor.Value);
    }

    var items = await query
        .Take(pageSize + 1)
        .Select(x => new EventDto(x.Id, x.Name, x.CreatedAtUtc))
        .ToListAsync(ct);

    var hasMore = items.Count > pageSize;
    var pageItems = hasMore ? items.Take(pageSize).ToList() : items;
    var nextCursor = hasMore ? pageItems[^1].CreatedAtUtc : (DateTimeOffset?)null;

    return Results.Ok(new { items = pageItems, nextCursor, hasMore });
});
```

That gives clients stable forward navigation without deep `Skip()` costs.

## Keep Ordering Deterministic

Always include a tie-breaker (like `Id`) after timestamp ordering. Without that, two rows with identical timestamps can flip order across requests.

## Response Envelope

I usually return metadata with items:

```json
{
  "items": [ ... ],
  "nextCursor": "2026-05-18T10:40:22Z",
  "hasMore": true
}
```

It keeps client pagination logic simple and explicit.

## Wrapping Up

Offset pagination is fine for many internal screens. Cursor pagination is better for scale, consistency, and user-facing feeds.

If you're seeing slow deep-page queries or duplicate records between pages, it's probably time to switch.
