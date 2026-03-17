---
title: LINQ Best Practices in C#
date: 2026-03-17
tags: dotnet, csharp, linq, performance
image: linq-best-practices-in-csharp.png
---

LINQ is one of C#'s most expressive features — you can filter, transform, and aggregate collections with code that reads almost like English. But it's also one of the easiest places to write code that silently performs badly. Deferred execution, multiple enumeration, and N+1 queries are all traps that look harmless and surface as production issues.

This post covers the LINQ patterns I reach for every day, the pitfalls I've seen bite real teams, and the rules of thumb that keep things fast and readable.

## Deferred Execution: The Most Important Concept

LINQ queries don't run when you write them. They run when you enumerate the result. This is called deferred execution, and understanding it is the key to using LINQ well.

```csharp
var numbers = new List<int> { 1, 2, 3, 4, 5 };

// This does NOT filter yet — it builds a query
var evens = numbers.Where(n => n % 2 == 0);

numbers.Add(6);  // modifying the source AFTER defining the query

// The query runs HERE, on the current state of numbers (including 6)
foreach (var n in evens)
    Console.WriteLine(n);  // 2, 4, 6
```

Because enumeration is deferred, the query always sees the latest state of the source. That's often useful, but it can also surprise you. If you capture a query in a variable and enumerate it multiple times, it re-executes each time — including any side effects or database calls inside it.

When you want a snapshot, materialise the query immediately with `ToList()` or `ToArray()`:

```csharp
// Materialise once, enumerate many times safely
var evens = numbers.Where(n => n % 2 == 0).ToList();
```

## Multiple Enumeration: A Sneaky Performance Trap

Multiple enumeration is what happens when you enumerate an `IEnumerable\<T\>` more than once without materialising it first. If the source is a database query or an expensive computation, you're doing that work multiple times.

```csharp
IEnumerable<Order> GetPendingOrders() => _db.Orders.Where(o => o.Status == "Pending");

var orders = GetPendingOrders();

// Each of these enumerates the source query separately — three database round trips
var count = orders.Count();
var total = orders.Sum(o => o.Amount);
var recent = orders.OrderByDescending(o => o.CreatedAt).Take(5).ToList();
```

Roslyn analysers like those in `Microsoft.CodeAnalysis.NetAnalyzers` will flag this. But even without tooling, the pattern is easy to spot: multiple LINQ calls on the same `IEnumerable\<T\>` variable.

Fix it by materialising first:

```csharp
var orders = GetPendingOrders().ToList();  // one database round trip

var count = orders.Count;
var total = orders.Sum(o => o.Amount);
var recent = orders.OrderByDescending(o => o.CreatedAt).Take(5).ToList();
```

The rule: if you're going to use a sequence more than once, call `ToList()` or `ToArray()` before you do.

## ToList() vs ToArray(): When It Matters

Both materialise a query, so both fix multiple enumeration. The difference is in the resulting type.

`ToList()` gives you a `List\<T\>` — mutable, and with O(1) `Count` and index access. `ToArray()` gives you a `T[]` — slightly more memory-efficient, and a better choice when you know the data won't change.

```csharp
// Use ToList() when you might add/remove items later
var items = query.ToList();
items.Add(extraItem);

// Use ToArray() for read-only snapshots — marginally leaner
var snapshot = query.ToArray();
```

For most application code, the difference barely matters. If you're in a hot path processing millions of items, `ToArray()` saves a small amount of memory and has better locality. For everyday work, pick whichever reads more clearly.

There's also `ToHashSet()` for when you need fast membership tests:

```csharp
var activeUserIds = _db.Users
    .Where(u => u.IsActive)
    .Select(u => u.Id)
    .ToHashSet();

// O(1) lookup instead of O(n) with Contains on a List
if (activeUserIds.Contains(userId)) { ... }
```

## Avoid N+1 Queries with EF Core

The N+1 problem is where LINQ meets data access and things go quietly wrong. You load a list of entities, then access a navigation property on each one — triggering a separate database query per item.

```csharp
// This looks innocent
var orders = await _db.Orders.ToListAsync();

foreach (var order in orders)
{
    // Each iteration hits the database — one query per order
    Console.WriteLine(order.Customer.Name);
}
```

If you have 100 orders, that's 101 queries. With 1000 orders, 1001 queries. It scales terribly.

The fix is to eager-load the related data upfront with `Include`:

```csharp
var orders = await _db.Orders
    .Include(o => o.Customer)
    .ToListAsync();

// No extra queries — customer data is already loaded
foreach (var order in orders)
    Console.WriteLine(order.Customer.Name);
```

For more complex scenarios, project into a DTO rather than loading full entities:

```csharp
var summaries = await _db.Orders
    .Select(o => new OrderSummary
    {
        OrderId = o.Id,
        CustomerName = o.Customer.Name,
        Total = o.Lines.Sum(l => l.Quantity * l.UnitPrice)
    })
    .ToListAsync();
```

Projecting with `Select` tells EF Core exactly what columns to fetch — you get a leaner query and no risk of lazy-loading surprises.

## Method Syntax vs Query Syntax

LINQ has two syntaxes, and both compile to the same thing. Method syntax uses extension method chaining; query syntax looks like SQL.

```csharp
var names = new[] { "Alice", "Bob", "Charlie", "Dave" };

// Method syntax
var result = names
    .Where(n => n.Length > 3)
    .OrderBy(n => n)
    .Select(n => n.ToUpper());

// Query syntax — same result
var result = from n in names
             where n.Length > 3
             orderby n
             select n.ToUpper();
```

Method syntax is more widely used in modern C# codebases, and it's the only option for operators that don't have a query-syntax equivalent (`Zip`, `Aggregate`, `Distinct`, `Skip`, `Take`). Query syntax is occasionally cleaner for multi-source joins:

```csharp
// Query syntax can be clearer for joins
var orderLines = from o in orders
                 join c in customers on o.CustomerId equals c.Id
                 where o.Total > 100
                 select new { o.Id, c.Name, o.Total };
```

My rule: use method syntax by default; switch to query syntax when a join or complex grouping reads more clearly.

## Use Select to Project Early

When working with collections of objects, project into the shape you actually need as early as possible. This limits the amount of data flowing through the pipeline and keeps transformations explicit.

```csharp
// Carrying full Product objects through the pipeline — unnecessary
var names = products
    .Where(p => p.IsAvailable)
    .OrderBy(p => p.Name)
    .Select(p => p.Name)
    .ToList();

// Better: same result, but Select happens first (where possible)
// In practice, Where before Select is more readable and often equivalent performance
var names = products
    .Where(p => p.IsAvailable)
    .Select(p => p.Name)
    .OrderBy(n => n)
    .ToList();
```

Against a database, projecting early is more impactful — EF Core translates the `Select` into a SQL `SELECT` clause, so only the projected columns are fetched. In-memory, projection saves object traversal but rarely changes performance significantly.

## Aggregate and Grouping

`GroupBy` is one of the more powerful LINQ operators and also one of the most misused. It returns `IEnumerable<IGrouping\<TKey, TElement\>>` — a sequence of groups, where each group has a key and a sequence of elements.

```csharp
var orders = GetOrders();

var byStatus = orders
    .GroupBy(o => o.Status)
    .Select(g => new
    {
        Status = g.Key,
        Count = g.Count(),
        Total = g.Sum(o => o.Amount)
    })
    .ToList();

foreach (var group in byStatus)
    Console.WriteLine($"{group.Status}: {group.Count} orders, £{group.Total:F2} total");
```

`Aggregate` is the general-purpose fold operation — useful when the built-in aggregators (`Sum`, `Min`, `Max`, `Count`) don't cover your case:

```csharp
var words = new[] { "C#", "is", "great" };

// Build a sentence by folding with a separator
var sentence = words.Aggregate((acc, next) => $"{acc} {next}");
// "C# is great"

// With a seed value
var csv = words.Aggregate("Words:", (acc, next) => $"{acc} {next}");
// "Words: C# is great"
```

## Materialise Before Modifying

One subtle trap: modifying a collection while an active LINQ query holds a reference to it. This throws an `InvalidOperationException` — "collection was modified; enumeration operation may not execute".

```csharp
var items = new List<int> { 1, 2, 3, 4, 5 };

// This throws — you're modifying items while iterating it
foreach (var item in items.Where(i => i % 2 == 0))
    items.Remove(item);
```

The fix is to materialise the query before the loop:

```csharp
var toRemove = items.Where(i => i % 2 == 0).ToList();

foreach (var item in toRemove)
    items.Remove(item);
```

Or, even cleaner, use `RemoveAll`:

```csharp
items.RemoveAll(i => i % 2 == 0);
```

## Performance Tips for Hot Paths

In most application code, LINQ's overhead is negligible. In hot paths — tight loops, high-throughput request handling, data pipelines — a few habits help.

**Prefer `Any()` over `Count() > 0`**. `Any()` short-circuits on the first matching element; `Count()` walks the entire sequence.

```csharp
// Count() walks everything — O(n)
if (items.Count() > 0) { ... }

// Any() stops at the first match — O(1) for non-empty collections
if (items.Any()) { ... }

// Same applies for conditions
if (items.Any(i => i.IsActive)) { ... }  // stops at first active item
```

**Use `FirstOrDefault()` instead of `Where().FirstOrDefault()`** when you have a simple predicate — it's equivalent but slightly more direct:

```csharp
// Equivalent, but the second is cleaner
var item = items.Where(i => i.Id == id).FirstOrDefault();
var item = items.FirstOrDefault(i => i.Id == id);
```

**Avoid LINQ in tight loops**. If you're calling a LINQ query millions of times per second, the delegate allocations and method call overhead add up. Prefer plain loops in those cases, and profile before optimising — most code isn't in that category.

**Use `IReadOnlyList\<T\>` return types when the result is materialised**. This makes it clear to callers that enumeration is already done:

```csharp
// Signals to callers that the result is already materialised
public IReadOnlyList<Product> GetAvailableProducts()
{
    return _products
        .Where(p => p.IsAvailable)
        .OrderBy(p => p.Name)
        .ToList();
}
```

## Putting It All Together

LINQ is a powerful tool, but it rewards people who understand how it works, not just what it does.

The short version:
- Understand deferred execution — queries run when enumerated, not when defined
- Materialise with `ToList()` or `ToArray()` when you'll enumerate more than once
- Pick `ToHashSet()` when you need fast membership tests
- Use `Include` in EF Core to avoid N+1 queries; project with `Select` to limit fetched data
- Prefer method syntax; use query syntax for complex joins
- `Any()` beats `Count() > 0` for non-empty checks
- Return `IReadOnlyList\<T\>` from methods that materialise their results

Get these habits in place and LINQ becomes one of the most productive parts of the C# toolbox — expressive, composable, and fast enough for almost everything you'll throw at it.
