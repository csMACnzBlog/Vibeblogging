---
title: "Practical Guide to Lazy in .NET"
date: "2026-05-30"
tags: "dotnet, csharp, performance, concurrency"
image: "practical-guide-to-lazy-initialization-in-dotnet.png"
---

I used to initialise everything at startup because it felt "safe." Then I'd profile cold starts and realise half those objects were never used on most requests.

`Lazy<T>` fixed that for me. It's simple, but there are a few gotchas worth knowing before you sprinkle it everywhere.

## The basic pattern

Here's the shape I start with when creation is expensive:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;

public sealed class PricingCache
{
    private readonly Lazy<Dictionary<string, decimal>> _rates =
        new(() => LoadRatesFromDisk());

    public decimal GetRate(string currency) => _rates.Value[currency];

    private static Dictionary<string, decimal> LoadRatesFromDisk()
    {
        // Simulate expensive IO
        Thread.Sleep(200);
        return new Dictionary<string, decimal>
        {
            ["USD"] = 1.0m,
            ["EUR"] = 0.92m,
            ["GBP"] = 0.79m
        };
    }
}
```

Nothing is loaded until `GetRate` touches `.Value`. If the code path never runs, you never pay the cost.

## Pick the right thread-safety mode

Most of the time, the default is exactly what you want. But when contention is high, I pick mode explicitly so intent is obvious:

```csharp
using System;
using System.Threading;

public static class MetricsProvider
{
    private static readonly Lazy<DateTimeOffset> StartedAt = new(
        valueFactory: () => DateTimeOffset.UtcNow,
        mode: LazyThreadSafetyMode.ExecutionAndPublication);

    public static DateTimeOffset StartTime => StartedAt.Value;
}
```

My quick rule of thumb:

- `ExecutionAndPublication` (default): one winner initialises, everyone else waits
- `PublicationOnly`: multiple threads may race to create, one result is published
- `None`: no locking; only safe when you control access to one thread

## Know what happens on exceptions

One thing that surprised me early on: if the value factory throws, that exception is cached for default mode.

```csharp
using System;
using System.IO;

private static readonly Lazy<string> Config = new(() =>
{
    var path = Environment.GetEnvironmentVariable("APP_CONFIG_PATH");
    if (string.IsNullOrWhiteSpace(path))
    {
        throw new InvalidOperationException("APP_CONFIG_PATH is missing.");
    }

    return File.ReadAllText(path);
});
```

If this fails once, future `.Value` calls rethrow the same failure. That's usually good (fail fast and consistently), but it's worth designing for.

## Where I avoid `Lazy<T>`

I don't use it for tiny object graphs or hot-path allocations where the lock/check overhead outweighs any startup savings. It's best when creation is expensive *and* not always needed.

## Final thought

`Lazy<T>` isn't flashy, but it's one of those pragmatic tools that can shave startup time and reduce wasted work with very little code. If you've got expensive dependencies that are only used in specific flows, it's a great fit.
