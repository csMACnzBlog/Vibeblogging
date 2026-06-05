---
title: SemaphoreSlim in C#: Throttling Async Work
date: 2026-06-05
tags: csharp, dotnet, async, performance
image: semaphoreslim-in-csharp-throttling-async-work.png
---

If you've ever kicked off a bunch of async work with `Task.WhenAll` and then watched your app hammer a downstream service, you've already met the problem that `SemaphoreSlim` solves.

I reach for it when I want concurrency, but not *unlimited* concurrency. That's the key idea: let some work run in parallel while keeping a hard cap on how many operations can be active at once.

## What `SemaphoreSlim` actually gives you

At its simplest, `SemaphoreSlim` is a counter.

- If the counter says a slot is available, your code can enter.
- If not, it waits until another operation finishes and releases its slot.

That makes it a nice fit for things like:

- throttling outbound HTTP calls
- limiting concurrent file processing
- protecting a scarce resource such as a shared connection
- smoothing out bursts of background work

Here's the smallest useful example:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

var semaphore = new SemaphoreSlim(initialCount: 2, maxCount: 2);

async Task ProcessAsync(int id)
{
    await semaphore.WaitAsync();

    try
    {
        Console.WriteLine($"Starting {id}");
        await Task.Delay(TimeSpan.FromSeconds(1));
        Console.WriteLine($"Finished {id}");
    }
    finally
    {
        semaphore.Release();
    }
}

List<Task> tasks =
[
    ProcessAsync(1),
    ProcessAsync(2),
    ProcessAsync(3),
    ProcessAsync(4)
];

await Task.WhenAll(tasks);
```

Even though four tasks are created, only two can be inside the protected section at the same time.

## The `try`/`finally` pattern matters

This is the part I never skip: if you call `WaitAsync`, you should almost always pair it with `Release` inside a `finally` block.

Without that, one exception can leak a slot and slowly turn your throttle into a deadlock factory.

```csharp
await semaphore.WaitAsync(cancellationToken);

try
{
    await DoWorkAsync(cancellationToken);
}
finally
{
    semaphore.Release();
}
```

That tiny pattern is the difference between "works in a demo" and "still works next month."

## A practical example: throttling API calls

Let's say you're enriching a batch of orders by calling another service. Running them one at a time is too slow. Running 500 at once is a great way to create your own outage.

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class OrderEnricher
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _semaphore = new(initialCount: 8, maxCount: 8);

    public OrderEnricher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task EnrichAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken)
    {
        List<Task> tasks = [];

        foreach (int orderId in orderIds)
        {
            tasks.Add(EnrichOneAsync(orderId, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task EnrichOneAsync(int orderId, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync($"/orders/{orderId}/details", cancellationToken);

            response.EnsureSuccessStatusCode();

            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Enriched order {orderId}: {payload.Length} characters");
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

What I like about this pattern is that it's boring in a good way. The concurrency limit is explicit, easy to tune, and lives right next to the code that needs it.

## `SemaphoreSlim` is not the same as `lock`

It's tempting to think of `SemaphoreSlim` as "async lock," but that only tells half the story.

- `lock` allows one caller in and blocks a thread.
- `SemaphoreSlim` can allow more than one caller in and supports `WaitAsync`.

If you need exactly one concurrent caller, you *can* initialize it with `1`:

```csharp
private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
```

That works well when you need async-friendly mutual exclusion. But once your goal becomes "allow a few operations, not just one," `SemaphoreSlim` starts to feel much more natural than trying to force everything through a single gate.

## Wrap the pattern when you repeat it

If you keep writing the same semaphore code around batches of work, a helper can keep call sites tidy.

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public static class ConcurrencyLimiter
{
    public static async Task ForEachAsync<T>(
        IEnumerable<T> source,
        int maxConcurrency,
        Func<T, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        List<Task> tasks = [];

        foreach (T item in source)
        {
            tasks.Add(RunOneAsync(item, semaphore, action, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private static async Task RunOneAsync<T>(
        T item,
        SemaphoreSlim semaphore,
        Func<T, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            await action(item, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

Now the calling code gets simpler:

```csharp
int[] customerIds = [101, 102, 103, 104, 105];

await ConcurrencyLimiter.ForEachAsync(
    customerIds,
    maxConcurrency: 3,
    async (customerId, cancellationToken) =>
    {
        await Task.Delay(250, cancellationToken);
        Console.WriteLine($"Processed customer {customerId}");
    });
```

## A couple of easy mistakes to avoid

The common mistakes are pretty predictable:

1. **Forgetting `finally`** and leaking permits.
2. **Using a huge concurrency limit** that isn't meaningfully different from no limit at all.
3. **Creating a new semaphore per operation** instead of sharing one across the whole batch or service.
4. **Assuming it enforces rate limits over time**. It doesn't — it limits *concurrent work*, not requests per second.

That last one matters. If you need "no more than 50 calls per minute," you want rate limiting. If you need "no more than 5 calls in flight at once," `SemaphoreSlim` is a great fit.

## Final thought

`SemaphoreSlim` is one of those tools that's easy to underestimate because the API is so small. But if your async code needs a little discipline, it's often the cleanest fix.

You don't have to choose between sequential and chaotic. A small concurrency limit in the right place usually gives you the sweet spot in the middle.
