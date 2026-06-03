---
title: Delegates & Events: Beyond Callbacks
date: 2026-06-03
tags: csharp, dotnet, events, architecture
image: working-with-delegates-and-events-in-csharp.png
---

If you’ve been writing C# for a while, you’ve used delegates and events — but it’s easy to treat them as “that thing UI frameworks do” and move on.

I used to do that too. Then I started building more modular services and realized delegates/events are one of the cleanest ways to decouple behavior *without* pulling in a full message bus.

Let’s walk through practical patterns you can use today.

## Delegates as behavior slots

A delegate is just a type-safe function reference. That sounds small, but it gives you a flexible seam for injecting behavior.

```csharp
using System;

public static class RetryRunner
{
    public static T RunWithRetry<T>(Func<T> operation, int maxAttempts)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        Exception? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Operation failed after retries.", lastError);
    }
}
```

The key point: `RunWithRetry` knows nothing about *what* it runs. You pass behavior in, and the utility handles policy.

## Prefer `Func`/`Action` for simple contracts

You don’t always need a custom delegate type. `Func<>` and `Action<>` keep things compact when the intent is obvious.

```csharp
using System;
using System.Collections.Generic;

public sealed class Pipeline
{
    private readonly List<Action<string>> _steps = [];

    public void AddStep(Action<string> step)
    {
        if (step is null) throw new ArgumentNullException(nameof(step));
        _steps.Add(step);
    }

    public void Execute(string input)
    {
        foreach (Action<string> step in _steps)
        {
            step(input);
        }
    }
}
```

This pattern works great for light processing pipelines, validation chains, and customizable hooks.

## Events are publish/subscribe with guardrails

Events wrap delegates with a constraint: external code can subscribe/unsubscribe, but can’t invoke the event directly. That makes your API safer.

```csharp
using System;

public sealed class JobProcessor
{
    public event EventHandler<JobCompletedEventArgs>? JobCompleted;

    public void Process(Guid jobId)
    {
        // Simulate work...
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        OnJobCompleted(new JobCompletedEventArgs(jobId, completedAt));
    }

    private void OnJobCompleted(JobCompletedEventArgs args)
    {
        JobCompleted?.Invoke(this, args);
    }
}

public sealed class JobCompletedEventArgs : EventArgs
{
    public JobCompletedEventArgs(Guid jobId, DateTimeOffset completedAt)
    {
        JobId = jobId;
        CompletedAt = completedAt;
    }

    public Guid JobId { get; }
    public DateTimeOffset CompletedAt { get; }
}
```

That null-conditional `?.Invoke` is important: if nobody subscribed, nothing happens.

## Don’t forget to unsubscribe when lifetimes differ

The most common event bug I see is accidental retention: a long-lived publisher keeps a short-lived subscriber alive.

```csharp
using System;

public sealed class MetricsReporter : IDisposable
{
    private readonly JobProcessor _processor;

    public MetricsReporter(JobProcessor processor)
    {
        _processor = processor;
        _processor.JobCompleted += OnJobCompleted;
    }

    private void OnJobCompleted(object? sender, JobCompletedEventArgs e)
    {
        Console.WriteLine($"Job {e.JobId} finished at {e.CompletedAt:O}");
    }

    public void Dispose()
    {
        _processor.JobCompleted -= OnJobCompleted;
    }
}
```

If you subscribe in a constructor, it’s usually a good hint that you should unsubscribe in `Dispose`.

## A practical rule of thumb

Here’s what I use:

- Use delegates (`Func`/`Action`) when a caller provides behavior to *one* callee.
- Use events when *many* listeners react to something that happened.
- Keep event args small and focused on facts, not commands.
- Be explicit about subscriber lifetime to avoid leaks.

## Final thought

Delegates and events aren’t old-school leftovers — they’re still sharp tools for building decoupled, testable C# code.

If your class is growing lots of “also do this” branches, that’s a smell. A delegate seam or an event hook often gives you cleaner extension points with less friction.
