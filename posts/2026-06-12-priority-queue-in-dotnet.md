---
title: "PriorityQueue in .NET: Ordered Processing"
date: 2026-06-12
tags: csharp, dotnet, collections, performance
image: priority-queue-in-dotnet.png
---

Sometimes order matters — not insertion order, but *priority* order. You want the highest-priority item next, regardless of when it arrived. That's what `PriorityQueue<TElement, TPriority>` is for.

It shipped in .NET 6 and it's straightforward once you understand the one twist: priority is a separate value from the element.

## The basics

You enqueue items with an element and a priority. Dequeue always returns the element with the *lowest* priority value first — it's a min-heap by default.

```csharp
var queue = new PriorityQueue<string, int>();

queue.Enqueue("low urgency task", 10);
queue.Enqueue("critical task", 1);
queue.Enqueue("medium task", 5);

while (queue.TryDequeue(out var task, out var priority))
{
    Console.WriteLine($"[{priority}] {task}");
}
// Output:
// [1] critical task
// [5] medium task
// [10] low urgency task
```

The queue doesn't care about insertion order at all. Only the priority value drives what comes out next.

## Why the element and priority are separate

This design lets you use any comparable type as a priority without constraining the element type. Want float priorities? Custom comparer? Done.

```csharp
var queue = new PriorityQueue<WorkItem, double>(
    Comparer<double>.Create((a, b) => b.CompareTo(a)) // highest first
);

queue.Enqueue(new WorkItem("sync metadata"), 0.3);
queue.Enqueue(new WorkItem("process payment"), 0.99);
queue.Enqueue(new WorkItem("send welcome email"), 0.5);

queue.TryDequeue(out var next, out _);
Console.WriteLine(next.Name); // process payment
```

Passing a comparer into the constructor flips the heap — now highest value wins. You can use this to build max-heaps without extra wrappers.

## Peeking without removing

`TryPeek` lets you inspect the front of the queue without consuming it:

```csharp
var queue = new PriorityQueue<string, int>();
queue.Enqueue("background sync", 100);
queue.Enqueue("user request", 1);

if (queue.TryPeek(out var top, out var topPriority))
{
    Console.WriteLine($"Next up: {top} (priority {topPriority})");
    // Next up: user request (priority 1)
}
```

This is handy when you want to check whether the next item meets some threshold before actually dequeuing.

## A practical example: task scheduler

Here's a minimal priority-aware task scheduler. Low numbers run first.

```csharp
public sealed class PriorityScheduler
{
    private readonly PriorityQueue<Func<Task>, int> _queue = new();

    public void Schedule(Func<Task> work, int priority)
        => _queue.Enqueue(work, priority);

    public async Task RunAllAsync()
    {
        while (_queue.TryDequeue(out var work, out _))
        {
            await work();
        }
    }
}
```

Usage:

```csharp
var scheduler = new PriorityScheduler();

scheduler.Schedule(() => SendAlert("disk space low"), priority: 1);
scheduler.Schedule(() => ArchiveLogs(), priority: 50);
scheduler.Schedule(() => GenerateReport(), priority: 20);

await scheduler.RunAllAsync();
// Runs: SendAlert → GenerateReport → ArchiveLogs
```

Real schedulers get more complex, but this pattern gets you a long way.

## EnqueueRange for batch inserts

If you have a batch of items to add, `EnqueueRange` is cleaner than a loop and slightly more efficient:

```csharp
var tasks = new[]
{
    ("index search", 5),
    ("rebuild thumbnails", 30),
    ("notify user", 2),
};

queue.EnqueueRange(tasks);
```

Each tuple is `(element, priority)`. The internal heap adjusts once rather than rebalancing after every individual enqueue.

## What it isn't

`PriorityQueue<TElement, TPriority>` doesn't support random removal or priority updates. If you enqueue a task at priority 10 and then need to promote it to priority 1, you can't do that in place — you'd need to rebuild or use a different structure.

It also doesn't have a concurrent version in the BCL. For multi-threaded scenarios you'd need your own locking or a third-party implementation.

## When to reach for it

It's the right tool when:

- You need to process items in priority order, not arrival order
- The priority is external to the element itself
- You want a clean, allocation-friendly BCL option without pulling in a library

For simple "always grab the min" cases it replaces hand-rolled sorted lists or heaps entirely. And because it's generic, it stays ergonomic across any element type.
