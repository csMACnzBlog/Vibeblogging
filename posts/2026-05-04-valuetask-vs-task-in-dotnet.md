---
title: ValueTask vs Task in .NET
date: 2026-05-04
tags: dotnet, csharp, async, performance, tutorial
image: valuetask-vs-task-in-dotnet.png
---

`Task` is the bread and butter of async .NET — you use it everywhere without thinking twice. But there's another option that's been in the framework since .NET Core 2.0 and is worth knowing: `ValueTask`. It's not a replacement for `Task`, but in the right places it can eliminate unnecessary allocations and make hot-path async code meaningfully faster.

## The Allocation Problem with Task

Every time you `return Task.FromResult(value)` or `await` a method that completes synchronously, the runtime allocates a `Task` object on the heap. For code that runs occasionally, that's fine — one allocation doesn't matter. But for code that runs thousands of times per second (cache lookups, frequently-hit API endpoints, tight loops), those allocations add up. You'll see GC pressure, longer pause times, and reduced throughput.

Here's the classic example — a cache-first repository:

```csharp
public async Task<Product?> GetProductAsync(int id)
{
    if (_cache.TryGetValue(id, out var product))
        return product; // Allocates a Task<Product?> every time

    var result = await _database.QueryAsync<Product>(
        "SELECT * FROM Products WHERE Id = @id", new { id });

    _cache[id] = result;
    return result;
}
```

The cache hit path never actually does any async work, but it still pays the cost of a `Task` allocation on every call. If this method runs a million times per minute and the cache hit rate is 99%, that's 990,000 unnecessary allocations per minute.

## ValueTask to the Rescue

`ValueTask<T>` is a struct — it lives on the stack when the operation completes synchronously, so there's no heap allocation:

```csharp
public ValueTask<Product?> GetProductAsync(int id)
{
    if (_cache.TryGetValue(id, out var product))
        return ValueTask.FromResult(product); // No allocation

    return new ValueTask<Product?>(GetFromDatabaseAsync(id));
}

private async Task<Product?> GetFromDatabaseAsync(int id)
{
    var result = await _database.QueryAsync<Product>(
        "SELECT * FROM Products WHERE Id = @id", new { id });

    _cache[id] = result;
    return result;
}
```

The synchronous path now returns a `ValueTask<Product?>` that wraps the value directly. The async path still uses a `Task` under the covers (via `GetFromDatabaseAsync`), but that only happens on cache misses. You're paying the allocation cost only when you actually need it.

## How ValueTask Works Internally

`ValueTask<T>` is a discriminated union of three things:
1. A plain `T` value (synchronous result, no allocation)
2. A `Task<T>` (async result, standard heap allocation)
3. An `IValueTaskSource<T>` (async result with pooled state machine, advanced use)

The struct itself always lives on the stack at the call site. Only the third option reaches beyond that, and it's used by async state machine pooling in high-performance scenarios.

```csharp
// These are all valid ValueTask<int> returns:
return new ValueTask<int>(42);                   // Direct value - no allocation
return new ValueTask<int>(someTask);             // Wraps an existing Task<int>
return ValueTask.FromResult(42);                 // Convenience method, same as above
return ValueTask.FromException<int>(ex);         // Completed with exception
return ValueTask.FromCanceled<int>(token);       // Completed as cancelled
```

## The IValueTaskSource Pattern

For the absolute highest-performance cases, you can implement `IValueTaskSource<T>` yourself and pool the state machine object to avoid allocations even on the async path. This is what .NET's own networking stack does. It's complex, but the pattern looks like this:

```csharp
// This is advanced territory - only go here if profiling shows it's worth it
public class PooledOperation : IValueTaskSource<int>
{
    private ManualResetValueTaskSourceCore<int> _core;

    public ValueTask<int> ExecuteAsync()
    {
        // ... set up the operation
        return new ValueTask<int>(this, _core.Version);
    }

    int IValueTaskSource<int>.GetResult(short token)
        => _core.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<int>.GetStatus(short token)
        => _core.GetStatus(token);

    void IValueTaskSource<int>.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
```

`ManualResetValueTaskSourceCore<T>` does the heavy lifting — you just need to call `_core.SetResult(value)` when the operation completes. This is how `Socket`, `NetworkStream`, and similar types achieve near-zero allocation async I/O. You probably won't write this yourself, but it's good to know it exists.

## What Not to Do with ValueTask

`ValueTask` has a critical restriction: **you can only await it once**. Unlike `Task`, it doesn't cache its result for repeated awaits or multiple continuations.

```csharp
// DON'T DO THIS
var valueTask = GetProductAsync(42);
var result1 = await valueTask; // Fine
var result2 = await valueTask; // Undefined behaviour - may throw, may corrupt state
```

Similarly, don't store a `ValueTask` and await it later if the underlying `IValueTaskSource` might have been recycled by then:

```csharp
// DON'T DO THIS EITHER
ValueTask<int> stored = SomeMethod();
// ... do other stuff ...
var result = await stored; // Potentially dangerous if it wraps an IValueTaskSource
```

If you need to await multiple times or store for later, convert to a `Task` first:

```csharp
var task = GetProductAsync(42).AsTask();
var result1 = await task;
var result2 = await task; // Fine - Task caches its result
```

## When to Use ValueTask

The rule is straightforward: use `ValueTask<T>` when the synchronous completion path is expected to be **more common** than the asynchronous path.

Good candidates:
- **Cache-first reads** — most requests hit the cache
- **Value object validation** — usually fast, occasionally async
- **Short-circuit logic** — a guard clause that returns early most of the time
- **Frequently-called interface methods** where implementations may be sync

Stick with `Task<T>` for:
- **Operations that are almost always async** — network I/O, disk reads, DB writes
- **Any method where callers might await multiple times**
- **Public APIs where you're not sure how callers will use the result**
- **Most application-layer code** — the overhead of `Task` rarely matters outside hot paths

Here's a simple heuristic: if BenchmarkDotNet or a profiler shows allocations from `Task.FromResult` on a hot path, it's time to consider `ValueTask`. Otherwise, `Task` is the right default.

## A Quick Benchmark

To make this concrete, here's what BenchmarkDotNet typically shows for the cache hit scenario:

```csharp
[Benchmark]
public async Task<int> TaskCacheHit()
{
    return await _taskRepository.GetAsync(_existingKey);
}

[Benchmark]
public async ValueTask<int> ValueTaskCacheHit()
{
    return await _valueTaskRepository.GetAsync(_existingKey);
}
```

In a microbenchmark on .NET 8 you'd expect to see something like:

```
| Method            | Mean     | Allocated |
|------------------ |---------:|----------:|
| TaskCacheHit      | 45.2 ns  | 72 B      |
| ValueTaskCacheHit |  5.8 ns  | 0 B       |
```

Zero allocations, ~8x faster on the synchronous path. Whether that matters in practice depends entirely on how hot the path is — but when it matters, it matters a lot.

## Wrapping Up

`ValueTask<T>` isn't a replacement for `Task<T>` — it's a precision tool. Use it when you know the synchronous completion path dominates and you've identified allocation overhead as a real concern. For everything else, `Task<T>` is simpler and safer.

The main things to remember:
- `ValueTask<T>` avoids heap allocation on synchronous completion
- Only await it once — convert to `Task` if you need more
- It's worth reaching for when profiling shows `Task.FromResult` allocations on a hot path
- The `IValueTaskSource<T>` pattern goes further but is only for advanced, performance-critical scenarios
