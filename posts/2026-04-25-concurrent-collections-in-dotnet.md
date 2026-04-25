---
title: Concurrent Collections in .NET
date: 2026-04-25
tags: dotnet, csharp, concurrency, tutorial
image: concurrent-collections-in-dotnet.png
---

Threading bugs are some of the nastiest bugs to track down. They appear intermittently, vanish under the debugger, and tend to manifest in production at the worst possible time. One common source of those bugs is using regular collections like `List<T>` or `Dictionary<TKey, TValue>` from multiple threads simultaneously — they're not thread-safe, and concurrent access leads to corrupted state.

The `System.Collections.Concurrent` namespace exists specifically to solve this. It provides thread-safe collection types that let you share data across threads without hand-rolling locking logic yourself.

## ConcurrentDictionary

This is the one you'll reach for most often. It's a thread-safe replacement for `Dictionary<TKey, TValue>` and is ideal for scenarios like caches, frequency counters, or tracking state keyed by some identifier.

```csharp
var cache = new ConcurrentDictionary<string, UserProfile>();

// Add or retrieve atomically
var profile = cache.GetOrAdd(userId, id => LoadProfileFromDatabase(id));
```

`GetOrAdd` is the killer feature. It atomically checks if the key exists and either returns the existing value or adds a new one. No lock needed at the call site.

For updating existing values safely, use `AddOrUpdate`:

```csharp
var hitCounts = new ConcurrentDictionary<string, int>();

// Increment the counter for a URL, adding it with 1 if not present
hitCounts.AddOrUpdate(
    key: url,
    addValue: 1,
    updateValueFactory: (key, existing) => existing + 1);
```

`TryGetValue`, `TryAdd`, and `TryRemove` are all atomic too. The one thing to watch: `GetOrAdd` with a factory delegate doesn't guarantee the factory runs exactly once. Under contention, multiple threads might evaluate the factory, but only one result gets stored. If the factory is expensive or has side effects, consider `Lazy<T>` inside the dictionary:

```csharp
var cache = new ConcurrentDictionary<string, Lazy<UserProfile>>();

var lazyProfile = cache.GetOrAdd(
    userId,
    id => new Lazy<UserProfile>(() => LoadProfileFromDatabase(id)));

var profile = lazyProfile.Value; // LoadProfile runs at most once
```

## ConcurrentQueue

`ConcurrentQueue<T>` is a first-in, first-out (FIFO) queue safe for concurrent producers and consumers. You enqueue with `Enqueue` and dequeue with `TryDequeue`:

```csharp
var queue = new ConcurrentQueue<WorkItem>();

// Producer thread
queue.Enqueue(new WorkItem(data));

// Consumer thread
if (queue.TryDequeue(out var item))
{
    Process(item);
}
```

`TryDequeue` returns `false` when the queue is empty rather than throwing, so you can poll without exception handling. `TryPeek` lets you inspect the front item without removing it.

`ConcurrentQueue` is well-suited for simple producer/consumer pipelines. For more complex scenarios where you want to block consumers until work arrives, look at `Channel<T>` (covered in a previous post) — it builds on the same idea but with proper async support.

## ConcurrentBag

`ConcurrentBag<T>` is an unordered collection optimised for scenarios where the same thread both adds and removes items. Think thread-local work pools where a thread processes its own items but occasionally steals work from other threads.

```csharp
var results = new ConcurrentBag<ProcessingResult>();

Parallel.ForEach(items, item =>
{
    var result = Process(item);
    results.Add(result);
});

// All results collected without explicit locking
foreach (var result in results)
{
    Console.WriteLine(result);
}
```

The thread-local optimisation means that if you're only ever adding from multiple threads and consuming from one at the end (as in the pattern above), `ConcurrentBag` performs well with minimal contention.

It's not the right choice if you need ordering guarantees or if you're doing a lot of cross-thread removes.

## ConcurrentStack

`ConcurrentStack<T>` is last-in, first-out (LIFO). Use it when you need stack semantics — undo history, depth-first traversal, that sort of thing — across multiple threads.

```csharp
var stack = new ConcurrentStack<int>();

stack.Push(1);
stack.Push(2);
stack.Push(3);

if (stack.TryPop(out var top))
{
    Console.WriteLine(top); // 3
}
```

It also supports pushing and popping multiple items at once with `PushRange` and `TryPopRange`, which can reduce overhead when you're working in bulk.

## BlockingCollection

`BlockingCollection<T>` is a wrapper around any `IProducerConsumerCollection<T>` (including `ConcurrentQueue` and `ConcurrentStack`) that adds blocking semantics. A consumer calling `Take` will block if the collection is empty, and a producer calling `Add` will block if a capacity limit has been reached.

```csharp
using var collection = new BlockingCollection<WorkItem>(boundedCapacity: 100);

// Producer task
var producer = Task.Run(() =>
{
    foreach (var item in GenerateWork())
    {
        collection.Add(item); // blocks if collection is full
    }
    collection.CompleteAdding(); // signal that no more items are coming
});

// Consumer task
var consumer = Task.Run(() =>
{
    foreach (var item in collection.GetConsumingEnumerable())
    {
        // GetConsumingEnumerable blocks until items are available
        // and completes when CompleteAdding is called
        Process(item);
    }
});

await Task.WhenAll(producer, consumer);
```

`CompleteAdding` is important — it's how you signal to consumers that the producer is done. Without it, `GetConsumingEnumerable` blocks forever waiting for more items that will never come.

The bounded capacity is also useful for backpressure: if your consumer can't keep up, the producer slows down instead of accumulating unbounded memory.

## Choosing the Right Collection

Here's a rough guide:

| Need | Collection |
|------|-----------|
| Key-value lookup / cache | `ConcurrentDictionary<TKey, TValue>` |
| Queue with async/await support | `Channel<T>` |
| Simple thread-safe FIFO queue | `ConcurrentQueue<T>` |
| Parallel results accumulation | `ConcurrentBag<T>` |
| Thread-safe stack | `ConcurrentStack<T>` |
| Blocking producer/consumer | `BlockingCollection<T>` |

One thing to avoid: wrapping `List<T>` or `Dictionary<TKey, TValue>` with a plain `lock` and calling it a day. For simple scenarios it works, but you lose composability — a caller can't atomically check-then-act because the lock is hidden inside your abstraction. The concurrent collection APIs expose atomic operations (`GetOrAdd`, `TryDequeue`, etc.) that let you write correct code at the call site.

## Wrapping Up

The `System.Collections.Concurrent` namespace covers the most common thread-safe collection needs in .NET. `ConcurrentDictionary` is the standout — it's genuinely useful in almost every multi-threaded application for caching, deduplication, or shared state. `BlockingCollection` is the right tool for classic producer/consumer pipelines where you want blocking behaviour without async.

For modern async code, `Channel<T>` builds on these foundations with a cleaner async API and more control over backpressure — but `BlockingCollection` is still worth knowing for synchronous workloads or when you're integrating with thread-based code rather than `Task`-based code.

Once you know these types are available, you'll stop reaching for `lock` around `Dictionary` and start writing concurrent code that's both correct and efficient.
