---
title: FrozenDictionary in .NET 8
date: 2026-05-15
tags: dotnet, csharp, performance, collections
image: frozen-collections-in-dotnet-8.png
---

Most of the time a `Dictionary<TKey, TValue>` does the job just fine. But if you've got a lookup table that's built once at startup and then read thousands of times a second, you're paying overhead for thread safety and general-purpose mutability that you'll never actually use.

.NET 8 introduced `FrozenDictionary<TKey, TValue>` and `FrozenSet<T>` specifically for this case. They're immutable after construction and optimised purely for read performance.

## The Idea

A regular dictionary handles inserts, updates, deletes, and concurrent reads. That flexibility costs something — even when none of those writes ever happen.

A frozen collection trades away that flexibility for faster lookups. The trade is only worth making when the collection is populated once and then used for the rest of the application's lifetime.

The classic example is a lookup table built from configuration:

```csharp
// Built once at startup
var permissions = new Dictionary<string, IReadOnlyList<string>>
{
    ["admin"]     = ["read", "write", "delete"],
    ["editor"]    = ["read", "write"],
    ["viewer"]    = ["read"],
};

// Then frozen and stored for repeated lookups
FrozenDictionary<string, IReadOnlyList<string>> frozenPermissions =
    permissions.ToFrozenDictionary();
```

After that, `frozenPermissions` behaves like a normal dictionary for reads — same `TryGetValue`, same indexer — just faster.

## Creating Frozen Collections

Both `FrozenDictionary` and `FrozenSet` are created from existing collections via extension methods:

```csharp
using System.Collections.Frozen;

// FrozenDictionary
var countryDialCodes = new Dictionary<string, string>
{
    ["NZ"] = "+64",
    ["AU"] = "+61",
    ["US"] = "+1",
    ["GB"] = "+44",
};

FrozenDictionary<string, string> dialCodes =
    countryDialCodes.ToFrozenDictionary();

// FrozenSet
var validCurrencyCodes = new HashSet<string> { "NZD", "AUD", "USD", "GBP", "EUR" };

FrozenSet<string> currencies = validCurrencyCodes.ToFrozenSet();
```

The extension methods live in `System.Collections.Frozen`, so you need that using directive.

## Looking Up Values

The API is what you'd expect:

```csharp
// Dictionary-style lookups
if (dialCodes.TryGetValue("NZ", out var code))
{
    Console.WriteLine($"NZ dial code: {code}"); // +64
}

string gbCode = dialCodes["GB"]; // "+44"

// Set-style membership checks
bool isValid = currencies.Contains("NZD"); // true
bool isNotValid = currencies.Contains("BTC"); // false
```

The public surface is just `IReadOnlyDictionary<TKey, TValue>` and `IReadOnlySet<T>` with some extra frozen-specific members. Your existing code that accepts those interfaces will work without modification.

## Registering as a Singleton

The most common pattern is registering a frozen collection as a singleton so it's built once when the application starts:

```csharp
builder.Services.AddSingleton<FrozenDictionary<string, string>>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    return config
        .GetSection("DialCodes")
        .GetChildren()
        .ToFrozenDictionary(
            section => section.Key,
            section => section.Value ?? string.Empty);
});
```

If you'd prefer to hide the frozen type from your consumers, register it as its interface:

```csharp
builder.Services.AddSingleton<IReadOnlyDictionary<string, string>>(sp =>
{
    // build and return frozenPermissions
});
```

## Key Comparison Options

Like a normal dictionary, you can control how keys are compared:

```csharp
// Case-insensitive string keys
FrozenDictionary<string, string> caseInsensitive =
    countryDialCodes.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

// Keys are compared ordinally by default if you don't specify
FrozenDictionary<string, string> ordinal =
    countryDialCodes.ToFrozenDictionary(StringComparer.Ordinal);
```

The comparer you pass in is baked into the frozen dictionary at construction time. The runtime can sometimes pick specialised lookup strategies based on the comparer, which is part of where the performance gains come from.

## When It's Worth Using

Frozen collections have a real cost: construction is slower than building a regular dictionary, because the type analyses the data to choose the best internal representation. That cost is paid once at startup.

The payoff is at read time. In benchmarks, `FrozenDictionary` lookups are meaningfully faster than `Dictionary` lookups, especially for small-to-medium collections with string keys.

That makes them a natural fit for:

- Feature flag tables loaded from configuration
- Permission or role lookups built from a database at startup
- Currency, country, or locale code lookups
- Any read-heavy mapping that never changes at runtime

They're not a replacement for `Dictionary` in general. If your collection grows or changes during the application's lifetime, frozen collections aren't the right tool.

## A Quick Practical Example

Here's a small service that uses a frozen dictionary for role-to-permission lookups:

```csharp
public sealed class PermissionService
{
    private readonly FrozenDictionary<string, FrozenSet<string>> _permissions;

    public PermissionService(
        FrozenDictionary<string, FrozenSet<string>> permissions)
    {
        _permissions = permissions;
    }

    public bool HasPermission(string role, string permission)
    {
        return _permissions.TryGetValue(role, out var rolePermissions)
            && rolePermissions.Contains(permission);
    }
}
```

And the registration:

```csharp
var permissionMap = new Dictionary<string, IEnumerable<string>>
{
    ["admin"]  = ["read", "write", "delete", "manage"],
    ["editor"] = ["read", "write"],
    ["viewer"] = ["read"],
};

builder.Services.AddSingleton(permissionMap
    .ToFrozenDictionary(
        kvp => kvp.Key,
        kvp => kvp.Value.ToFrozenSet(StringComparer.Ordinal),
        StringComparer.Ordinal));

builder.Services.AddSingleton<PermissionService>();
```

The `HasPermission` check is now about as fast as a lookup can get in managed code, with no locking overhead.

## Wrapping Up

`FrozenDictionary` and `FrozenSet` are narrow tools — but when the use case fits, they're a clean win. If you've got lookup tables that are populated once and read constantly, freezing them is low effort for a real performance improvement.

It's also one of those changes that makes the intent obvious. A `FrozenDictionary` in a constructor signature says "this is read-only by design", which is a useful signal on its own.
