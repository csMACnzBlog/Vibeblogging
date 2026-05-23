---
title: Reusing CancellationTokenSource with TryReset
date: 2026-05-23
tags: csharp, dotnet, performance, async
image: reusing-cancellationtokensource-with-tryreset.png
---

If you're creating a lot of short-lived operations, repeatedly allocating `CancellationTokenSource` instances can add avoidable pressure.

In .NET, `CancellationTokenSource.TryReset()` lets you reuse a source *when it's safe to do so*.

## The Basic Pattern

```csharp
var cts = new CancellationTokenSource();

for (var i = 0; i < 1000; i++)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeout.Token);

    await ProcessMessageAsync(queue[i], linked.Token);

    if (!cts.TryReset())
    {
        cts.Dispose();
        cts = new CancellationTokenSource();
    }
}
```

The important bit is the fallback. `TryReset()` can return `false`, and you should handle that path cleanly.

## When Reuse Is Safe

I treat reuse as an optimization only for controlled loops where:

- all work using the token has completed
- callbacks and registrations are no longer active
- no other thread still references the old token lifecycle

If those guarantees are fuzzy, just allocate a new source and keep things simple.

## A Practical Handler Example

```csharp
private CancellationTokenSource _loopCts = new();

public async Task RunBatchAsync(IReadOnlyList<Job> jobs, CancellationToken stopToken)
{
    foreach (var job in jobs)
    {
        stopToken.ThrowIfCancellationRequested();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_loopCts.Token, stopToken);
        await _processor.HandleAsync(job, linked.Token);

        if (!_loopCts.TryReset())
        {
            _loopCts.Dispose();
            _loopCts = new CancellationTokenSource();
        }
    }
}
```

This keeps the hot path lean while still preserving correctness.

## Wrapping Up

`TryReset()` is a useful micro-optimization for high-throughput loops, but only when lifecycle boundaries are crystal clear.

Use it where you're confident about ownership, keep the fallback allocation, and favor correctness over cleverness.
