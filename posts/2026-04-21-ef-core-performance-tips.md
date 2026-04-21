---
title: EF Core Performance Tips in .NET
date: 2026-04-21
tags: dotnet, csharp, efcore, tutorial
image: ef-core-performance-tips.png
---

Entity Framework Core is fantastic for getting database access up and running quickly. But if you're not careful, it can quietly generate some impressively inefficient SQL. The good news: most EF Core performance problems have straightforward fixes once you know what to look for.

## The N+1 Problem

This is the classic EF Core trap. You load a list of orders and then access each order's customer inside a loop:

```csharp
var orders = await _db.Orders.ToListAsync();

foreach (var order in orders)
{
    Console.WriteLine(order.Customer.Name); // triggers a separate query for each order!
}
```

That's one query to fetch orders, then one query per order to fetch the customer. With 100 orders, you just fired 101 queries. The fix is to use `Include` to load related data eagerly in a single query:

```csharp
var orders = await _db.Orders
    .Include(o => o.Customer)
    .ToListAsync();
```

Now it's one query with a join. Problem solved.

## Only Select What You Need

By default, `ToListAsync()` fetches every column in the table. If you've got a `Product` with 20 columns and you're only displaying the name and price in a dropdown, you're doing unnecessary work on both the database and the network.

Use projection to select only the fields you need:

```csharp
var products = await _db.Products
    .Select(p => new ProductSummary(p.Id, p.Name, p.Price))
    .ToListAsync();
```

This generates a `SELECT Id, Name, Price FROM Products` instead of a `SELECT *`. It's faster, uses less memory, and works even better at scale.

Projections also let you skip `Include` entirely for simple read scenarios — EF Core can walk navigation properties inside a `Select` without lazy or eager loading:

```csharp
var orders = await _db.Orders
    .Select(o => new OrderSummary(
        o.Id,
        o.Customer.Name,   // EF Core joins for you
        o.Total))
    .ToListAsync();
```

## Use AsNoTracking for Read-Only Queries

When EF Core returns entities, it tracks them in the change tracker by default. Every tracked entity costs memory, and when you call `SaveChangesAsync` it scans all of them for changes. For queries where you'll never modify the result, that's wasted effort.

`AsNoTracking()` tells EF Core to skip the change tracker entirely:

```csharp
var products = await _db.Products
    .AsNoTracking()
    .Where(p => p.CategoryId == categoryId)
    .ToListAsync();
```

For read-heavy workloads — like rendering a product listing page — this can make a measurable difference. It's low-effort and worth making a habit for any query that's purely for display.

You can also set it globally for a `DbContext` if you know the context is read-only:

```csharp
builder.Services.AddDbContext<ReadOnlyDbContext>(options =>
    options.UseSqlServer(connectionString)
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
```

## Filter in the Database, Not in Memory

This one trips up a lot of developers. If you call `ToListAsync()` and *then* filter, the filtering happens in memory after loading all the rows:

```csharp
// Bad: loads ALL products into memory first
var expensiveProducts = (await _db.Products.ToListAsync())
    .Where(p => p.Price > 100)
    .ToList();
```

Keep the `Where` clause before `ToListAsync` and EF Core will include it in the SQL:

```csharp
// Good: filter happens in the database
var expensiveProducts = await _db.Products
    .Where(p => p.Price > 100)
    .ToListAsync();
```

The same applies to `OrderBy`, `Skip`, and `Take` for pagination — always chain these before materialising the query.

## Pagination with Skip and Take

When you're loading lists for display, always paginate. Loading thousands of rows to show twenty is wasteful:

```csharp
public async Task<List<Product>> GetProductsPageAsync(int page, int pageSize)
{
    return await _db.Products
        .AsNoTracking()
        .OrderBy(p => p.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();
}
```

EF Core translates `Skip` and `Take` into `OFFSET`/`FETCH NEXT` (or equivalent for your database). Always include an `OrderBy` — without it, the results are non-deterministic and your pages might show duplicates or skip rows as data changes.

## Use Compiled Queries for Hot Paths

Every time EF Core processes a LINQ query, it has to translate it to SQL. This translation is cached, but if you have a very high-traffic query that runs thousands of times per second, even the cache lookup adds up.

Compiled queries let you pre-translate a query once at startup:

```csharp
private static readonly Func<AppDbContext, int, Task<Product?>> GetProductById =
    EF.CompileAsyncQuery((AppDbContext db, int id) =>
        db.Products.SingleOrDefault(p => p.Id == id));
```

Call it like a regular async method:

```csharp
var product = await GetProductById(_db, productId);
```

This skips translation entirely at runtime. For most apps this optimisation isn't necessary, but if you're profiling and database query translation appears in your hot path, compiled queries are the right tool.

## Bulk Operations with ExecuteUpdateAsync and ExecuteDeleteAsync

EF Core 7+ added `ExecuteUpdateAsync` and `ExecuteDeleteAsync`, which let you run bulk operations without loading entities into memory first.

Previously, to update all products in a category you'd load them, modify them, and save:

```csharp
// Old way: loads all matching products into memory
var products = await _db.Products
    .Where(p => p.CategoryId == categoryId)
    .ToListAsync();

foreach (var product in products)
    product.IsDiscontinued = true;

await _db.SaveChangesAsync();
```

With the new bulk APIs, you can do it in a single `UPDATE` statement:

```csharp
await _db.Products
    .Where(p => p.CategoryId == categoryId)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDiscontinued, true));
```

Similarly, `ExecuteDeleteAsync`:

```csharp
await _db.AuditLogs
    .Where(l => l.CreatedAt < cutoffDate)
    .ExecuteDeleteAsync();
```

No loading, no change tracking, no `SaveChangesAsync`. One round-trip to the database.

## Diagnosing Query Issues

You can't fix what you can't see. EF Core has a built-in way to log the SQL it generates — useful in development and staging:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString)
           .LogTo(Console.WriteLine, LogLevel.Information)
           .EnableSensitiveDataLogging()); // shows parameter values
```

For production, integrate with your regular logging setup:

```csharp
options.UseSqlServer(connectionString)
       .LogTo(
           (eventId, level) => level >= LogLevel.Warning,
           (data) => logger.Log(data.LogLevel, data.ToString()));
```

If you're seeing slow queries in production and can't reproduce them locally, BenchmarkDotNet (covered in a previous post) pairs well with EF Core for micro-benchmarking specific query paths.

## Wrapping Up

EF Core performance problems are usually not about EF Core itself — they're about how you use it. The most common culprits are N+1 queries, pulling back more data than you need, and filtering in memory instead of in the database.

A few habits cover the majority of cases: use `Include` or projection to load related data, add `AsNoTracking` to read-only queries, keep LINQ operators before `ToListAsync`, and always paginate lists. For bulk operations, reach for `ExecuteUpdateAsync` and `ExecuteDeleteAsync` instead of the load-modify-save pattern.

And when something feels slow, log the SQL. Once you can see what's going to the database, most problems become obvious.
