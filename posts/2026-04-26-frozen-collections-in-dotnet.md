---
title: Frozen Collections in .NET
date: 2026-04-26
tags: dotnet, csharp, performance, tutorial
image: frozen-collections-in-dotnet.png
---

Most collections in .NET are designed for mutation. You add items, remove them, update values. But there's a whole class of data that never changes after it's been set up — configuration lookups, country codes, HTTP status descriptions, permission mappings. For those cases, `Dictionary<TKey, TValue>` is overkill. It's thread-safe-ish, carries mutation overhead, and its internal structure isn't optimised for pure read access.

.NET 8 introduced `FrozenDictionary<TKey, TValue>` and `FrozenSet<T>` specifically for this use case: collections you build once and read from many times, potentially from multiple threads. They trade away the ability to change for significantly faster lookups.

## Creating a FrozenDictionary

You create a `FrozenDictionary` by calling `ToFrozenDictionary()` on any `IEnumerable<KeyValuePair<TKey, TValue>>` or using the LINQ-style overload:

```csharp
using System.Collections.Frozen;

var statusDescriptions = new Dictionary<int, string>
{
    [200] = "OK",
    [201] = "Created",
    [400] = "Bad Request",
    [401] = "Unauthorized",
    [403] = "Forbidden",
    [404] = "Not Found",
    [500] = "Internal Server Error",
}.ToFrozenDictionary();

// Or from any sequence:
var lookup = Enumerable.Range(1, 100)
    .ToFrozenDictionary(i => i, i => i * i);
```

Once created, the API mirrors `IReadOnlyDictionary<TKey, TValue>`. You get `TryGetValue`, indexer access, `ContainsKey`, `Keys`, `Values`, and `Count`. What you don't get is `Add`, `Remove`, or anything that mutates the collection.

```csharp
if (statusDescriptions.TryGetValue(statusCode, out var description))
{
    Console.WriteLine(description);
}

// Indexer throws KeyNotFoundException if not found, same as Dictionary
var message = statusDescriptions[404]; // "Not Found"
```

## Creating a FrozenSet

`FrozenSet<T>` is the frozen equivalent of `HashSet<T>`. Build it once from any enumerable:

```csharp
var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }
    .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

bool isAllowed = allowedExtensions.Contains(extension);
```

The comparer is captured at creation time, so `allowedExtensions.Contains(".PNG")` returns `true` in the example above.

`FrozenSet<T>` also supports set operations like `IsSubsetOf`, `Overlaps`, and `SetEquals`, which makes it useful for permission checks and capability matching:

```csharp
var requiredPermissions = new[] { "read", "write" }.ToFrozenSet();
var userPermissions = GetUserPermissions(); // returns IEnumerable<string>

if (requiredPermissions.IsSubsetOf(userPermissions))
{
    // user has all required permissions
}
```

## Why It's Faster

`Dictionary<TKey, TValue>` uses open addressing with chaining — a general-purpose design that handles the full lifecycle of a mutable collection well. `FrozenDictionary` knows at construction time exactly what keys it will ever contain. This lets the runtime pick from several specialised hash strategies based on the actual key distribution.

For small dictionaries, it can use a simple linear scan. For string keys, it can exploit properties of the specific strings present. For integer keys with a dense range, it can use direct array indexing. The exact strategy is an implementation detail, but the result is measurably faster `TryGetValue` on read-heavy workloads.

Here's a rough BenchmarkDotNet comparison for a dictionary with 500 string keys:

```
| Method               | Mean      | Allocated |
|--------------------- |----------:|----------:|
| Dictionary_TryGet    | 28.3 ns   | 0 B       |
| FrozenDictionary_TryGet | 14.1 ns | 0 B       |
```

Roughly 2× faster for lookups with zero extra allocation. The improvement is more pronounced for larger dictionaries and string keys, where the specialised hashing strategies have more room to help.

## A Real-World Pattern: Static Lookup Tables

The canonical use case is application startup — you build lookup tables that are read throughout the lifetime of the application:

```csharp
public static class CountryData
{
    public static readonly FrozenDictionary<string, string> CodeToName =
        new Dictionary<string, string>
        {
            ["US"] = "United States",
            ["GB"] = "United Kingdom",
            ["DE"] = "Germany",
            ["JP"] = "Japan",
            // ... hundreds more
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static readonly FrozenSet<string> EuMemberCodes =
        new[] { "AT", "BE", "BG", "CY", "CZ", "DE", /* ... */ }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
```

Because `FrozenDictionary` is immutable, it's safe to share across threads without any locking. No need for `ConcurrentDictionary` here — that's for collections that change at runtime.

## Registering with Dependency Injection

A common pattern in ASP.NET Core is to build frozen lookups at startup and register them as singletons:

```csharp
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    return config.GetSection("FeatureRoutes")
        .GetChildren()
        .ToFrozenDictionary(
            section => section.Key,
            section => section.Value ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
});
```

The frozen dictionary gets built once during startup, and every subsequent lookup is as fast as possible with no thread synchronisation overhead.

## Frozen vs ImmutableDictionary

You might wonder how `FrozenDictionary` compares to `ImmutableDictionary<TKey, TValue>` from `System.Collections.Immutable`. They're both immutable, but they solve different problems.

`ImmutableDictionary` is designed for persistent data structures — you can create modified versions cheaply using structural sharing. It's the right tool when you need immutable snapshots that evolve over time (think Redux-style state management or functional data structures).

`FrozenDictionary` is optimised purely for read performance. It doesn't support efficient creation of modified versions. If you need to "update" the data, you rebuild from scratch by calling `ToFrozenDictionary()` again.

The quick guide:

| Use case | Collection |
|----------|-----------|
| Build once, read many times | `FrozenDictionary` / `FrozenSet` |
| Immutable snapshots with cheap updates | `ImmutableDictionary` |
| Shared mutable across threads | `ConcurrentDictionary` |
| Single-threaded mutable | `Dictionary` |

## Wrapping Up

`FrozenDictionary<TKey, TValue>` and `FrozenSet<T>` fill a real gap in the .NET collections story. If you've got lookup tables that are set up at startup and then read constantly — country codes, route mappings, permission sets, configuration lookups — these types give you faster reads and thread safety with no extra ceremony.

The API is intentionally minimal. You build it with `ToFrozenDictionary()` or `ToFrozenSet()`, then use it like any read-only dictionary or set. The performance improvement is free — just swap the type and the runtime handles the rest.

If you haven't audited your `static readonly Dictionary` fields yet, it's worth checking which ones fit this profile. A one-line change to add `.ToFrozenDictionary()` at the end is all it takes.
