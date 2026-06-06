---
title: "PeriodicTimer: Tick Without Drift"
date: 2026-06-06
tags: csharp, dotnet, async, performance
image: periodic-timer-dotnet-tick-without-drift.png
---

If you've written a background loop that polls a database or calls an API on a schedule, you've probably done something like this:

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    await DoWorkAsync(cancellationToken);
    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
}
```

It works. But there's a quiet problem hidden in that loop.

Every iteration delays for 30 seconds *after the work finishes*. If `DoWorkAsync` takes 2 seconds, your actual interval is 32 seconds. Run this long enough and it drifts further from whatever schedule you intended.

`PeriodicTimer`, added in .NET 6, is designed specifically to avoid that problem.

## What `PeriodicTimer` actually does

Instead of delaying after each unit of work, `PeriodicTimer` fires on a fixed schedule. The timer advances independently of how long your work takes.

The API is deliberately small:

```csharp
using System.Threading;

using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

while (await timer.WaitForNextTickAsync(cancellationToken))
{
    await DoWorkAsync(cancellationToken);
}
```

`WaitForNextTickAsync` returns `true` when it's time to run, and `false` when the timer is disposed. Cancellation via `CancellationToken` throws `OperationCanceledException` as you'd expect.

Two things stand out here:

1. There's no `Task.Delay` in the loop. The timer manages its own schedule.
2. The pattern is async-first. No threads are blocked waiting for the next tick.

## How it handles slow work

The interesting part is what happens when your work runs longer than the period.

If your timer fires every 10 seconds and a particular run takes 15 seconds, `PeriodicTimer` doesn't queue up the missed tick. It just fires again as soon as the current call to `WaitForNextTickAsync` completes.

That means:

- No backlog of stacked ticks
- No surprise bursts of activity after a slow run
- The next tick always waits for you to ask for it

This makes it safe to use even when individual runs vary in length.

## Cancellation and shutdown

The standard approach is to pass a `CancellationToken` to `WaitForNextTickAsync`. When cancellation is requested, it throws `OperationCanceledException`.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

using var cts = new CancellationTokenSource();
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

// Cancel after 22 seconds in this example
cts.CancelAfter(TimeSpan.FromSeconds(22));

try
{
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Tick");
        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Timer stopped.");
}
```

You can also stop the timer cleanly by calling `Dispose()`. After disposal, the next call to `WaitForNextTickAsync` returns `false` instead of throwing. Either style works depending on whether you want a graceful exit or an exceptional one.

## A practical example: scheduled background polling

Here's a more realistic scenario. Suppose you want to poll a queue every 15 seconds inside a hosted service.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class QueuePollerService : BackgroundService
{
    private readonly IMessageQueue _queue;
    private readonly ILogger<QueuePollerService> _logger;

    public QueuePollerService(IMessageQueue queue, ILogger<QueuePollerService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                int processed = await _queue.ProcessPendingAsync(stoppingToken);
                _logger.LogInformation("Processed {Count} messages", processed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing queue messages");
                // Continue — the next tick will retry
            }
        }
    }
}
```

A few things worth noting:

- The timer is created inside `ExecuteAsync`, so it's scoped to the service lifetime.
- Exceptions are caught to keep the loop alive across transient failures.
- `OperationCanceledException` is not caught — it propagates out and signals shutdown cleanly.

## Comparing drift with `Task.Delay`

Let's make the drift concrete. If you're using a `Task.Delay` loop with a 10-second interval and each tick of work takes about 500ms:

| Run | Actual elapsed (Task.Delay) | Actual elapsed (PeriodicTimer) |
|-----|----------------------------|-------------------------------|
| 1   | ~10.5s                     | ~10s                          |
| 5   | ~52.5s                     | ~50s                          |
| 10  | ~105s                      | ~100s                         |
| 100 | ~1050s                     | ~1000s                        |

After 100 runs, the `Task.Delay` loop has drifted by an entire extra interval. `PeriodicTimer` stays on schedule.

For most applications the drift is harmless. But for rate-sensitive scenarios — syncing data at predictable intervals, sampling metrics, enforcing SLAs — it matters.

## One limitation to know

`PeriodicTimer` doesn't support a start delay. It fires immediately at the first tick after creation.

If you need to delay the first run, a simple workaround is an initial `Task.Delay` before the loop starts:

```csharp
await Task.Delay(initialDelay, cancellationToken);

using var timer = new PeriodicTimer(period);

while (await timer.WaitForNextTickAsync(cancellationToken))
{
    await DoWorkAsync(cancellationToken);
}
```

It's a minor inconvenience. For the vast majority of use cases, firing immediately on first tick is exactly what you want.

## Final thought

`PeriodicTimer` is a focused tool with a single job: fire on a schedule without stacking up or drifting. It's not trying to replace full-blown schedulers like Quartz.NET or Hangfire when you need cron expressions, persistence, or job management.

But for simple "do this every N seconds" loops in background services? It's the cleanest option in the standard library, and it's easy to reason about.

Replace your `Task.Delay` loop with one and you probably won't notice a difference — right up until the moment you would have.
