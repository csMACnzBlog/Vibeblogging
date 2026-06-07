---
title: The lock Statement in Modern C#
date: 2026-06-07
tags: csharp, dotnet, concurrency, threading
image: the-lock-statement-in-modern-csharp.png
---

If you've ever seen weird race-condition bugs where a value "sometimes" ends up wrong, you've already met the problem that `lock` solves.

`lock` is still one of the simplest tools for protecting shared in-memory state in C#. It's not flashy, but when multiple threads can update the same data, it keeps your code honest.

## Why race conditions happen

Imagine two requests incrementing the same counter at the same time. `count++` looks like one operation, but it's actually read, modify, write.

Without synchronization, those steps can interleave and you lose updates.

```csharp
using System;
using System.Threading.Tasks;

int count = 0;

Parallel.For(0, 100_000, _ =>
{
    count++;
});

Console.WriteLine(count); // Often less than 100000
```

You'll usually get a number lower than expected because multiple threads race through the same shared variable.

## The basic `lock` pattern

At its core, `lock` allows only one thread at a time into a critical section.

```csharp
using System;
using System.Threading.Tasks;

int count = 0;
object gate = new();

Parallel.For(0, 100_000, _ =>
{
    lock (gate)
    {
        count++;
    }
});

Console.WriteLine(count); // 100000
```

That's the core pattern:

1. Create a private lock object.
2. Wrap only the state mutation that must be protected.
3. Keep the locked section short.

## A practical example in a class

Here's what this usually looks like in real code.

```csharp
using System;

public sealed class InMemoryInventory
{
    private readonly object _gate = new();
    private int _stock;

    public InMemoryInventory(int initialStock)
    {
        _stock = initialStock;
    }

    public bool TryReserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        lock (_gate)
        {
            if (_stock < quantity)
            {
                return false;
            }

            _stock -= quantity;
            return true;
        }
    }

    public int CurrentStock
    {
        get
        {
            lock (_gate)
            {
                return _stock;
            }
        }
    }
}
```

A couple of things are doing real work here:

- The lock object is `private readonly` so no other code can lock on it.
- Both read and write paths are protected, so callers always see consistent values.

## What to lock on (and what not to)

A safe default is to lock on a dedicated private object field.

```csharp
private readonly object _gate = new();
```

Avoid locking on:

- `this`
- `typeof(SomeType)`
- string literals
- publicly accessible objects

Those can be locked by unrelated code, which creates deadlock risks that are painful to debug.

## Keep critical sections tiny

`lock` serializes access. That's the goal, but it also means everything inside the block is now a bottleneck.

Good:

```csharp
lock (_gate)
{
    _balance += amount;
}
```

Risky:

```csharp
lock (_gate)
{
    _balance += amount;
    File.AppendAllText("audit.log", $"Added {amount}{Environment.NewLine}");
    Thread.Sleep(200);
}
```

The second example keeps every other thread waiting while doing I/O and artificial delays. That's where throughput disappears.

## `lock` and async don't mix

This one catches people all the time: you can't `await` inside a `lock` block.

If your protected section needs async work, use an async-friendly primitive like `SemaphoreSlim` instead.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class TokenCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;

    public async Task<string> GetOrRefreshAsync(Func<Task<string>> refreshToken)
    {
        await _gate.WaitAsync();

        try
        {
            if (_token is null)
            {
                _token = await refreshToken();
            }

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

So the rule of thumb is simple:

- Shared synchronous in-memory mutation: `lock`
- Shared async flow: `SemaphoreSlim` (or another async coordination primitive)

## Final thought

`lock` isn't old-fashioned; it's focused. When you need thread-safe access to shared state in-process, it's usually the clearest solution.

Keep the lock target private, keep the critical section small, and avoid mixing it with async. Do those three things and you'll dodge most of the concurrency bugs people spend days chasing.
