---
title: WeakReference in .NET: Caches Without Leaks
date: 2026-06-04
tags: csharp, dotnet, memory, performance
image: weakreference-in-dotnet-caches-without-leaks.png
---

If you've ever built an in-memory cache, you've probably had the same thought I have: *I want this data to be easy to reuse, but I don't want it hanging around forever just because I looked it up once.*

That's exactly where `WeakReference<T>` can help. It lets you keep a reference to an object **without** preventing the garbage collector from reclaiming it.

That doesn't make it a magic cache. It does make it a handy tool for "reuse this if it's still around" scenarios.

## What a weak reference actually means

A normal reference says, "keep this object alive because I'm still using it."

A weak reference says, "I'd like to find this object again if it's still alive, but if memory pressure shows up, the GC can take it."

Here's the smallest useful example:

```csharp
using System;

byte[] imageBytes = LoadPreviewBytes();
var weakReference = new WeakReference<byte[]>(imageBytes);

if (weakReference.TryGetTarget(out byte[]? cachedBytes))
{
    Console.WriteLine($"Reused {cachedBytes.Length} bytes from memory.");
}
else
{
    Console.WriteLine("The cached value was collected. Rebuild it.");
}

static byte[] LoadPreviewBytes() => new byte[1024];
```

The important part is `TryGetTarget`. You always have to assume the object may already be gone.

That's the mental model shift: weak references are *opportunistic*, not guaranteed storage.

## A practical example: caching expensive previews

Let's say generating a document preview is expensive, but you don't want preview data to stay in memory just because one user opened a file five minutes ago.

```csharp
using System;
using System.Collections.Concurrent;

public sealed class DocumentPreviewCache
{
    private readonly ConcurrentDictionary<string, WeakReference<string>> _cache = new();

    public string GetPreview(string documentId)
    {
        if (_cache.TryGetValue(documentId, out WeakReference<string>? weakReference) &&
            weakReference.TryGetTarget(out string? preview))
        {
            return preview;
        }

        preview = BuildPreview(documentId);
        _cache[documentId] = new WeakReference<string>(preview);
        return preview;
    }

    private static string BuildPreview(string documentId)
    {
        Console.WriteLine($"Building preview for {documentId}");
        return $"Preview for {documentId}".PadRight(40, '.');
    }
}
```

I like this pattern because it makes the trade-off obvious:

- If the preview is still around, great — reuse it.
- If it was collected, rebuild it.
- The cache doesn't pretend it owns the object's lifetime.

That can be a nice fit for derived data, image thumbnails, parsed templates, or other values that are expensive to create but safe to recreate.

## Don't treat weak references like a normal cache

This is the part that's easy to get wrong.

If your application *needs* an item to stay available once cached, `WeakReference<T>` is the wrong tool. The GC decides when the object goes away, not you.

That means weak references are a poor fit for:

- session state
- critical configuration
- anything you can't cheaply recreate
- performance paths that need predictable hit rates

I treat `WeakReference<T>` as a bonus optimization, not as storage I can rely on.

## A reusable weak cache wrapper

If you find yourself repeating the same pattern, a small wrapper keeps the call sites tidy.

```csharp
using System;
using System.Collections.Concurrent;

public sealed class WeakCache<TKey, TValue> where TKey : notnull where TValue : class
{
    private readonly ConcurrentDictionary<TKey, WeakReference<TValue>> _entries = new();

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> valueFactory)
    {
        if (valueFactory is null) throw new ArgumentNullException(nameof(valueFactory));

        if (_entries.TryGetValue(key, out WeakReference<TValue>? weakReference) &&
            weakReference.TryGetTarget(out TValue? existingValue))
        {
            return existingValue;
        }

        TValue createdValue = valueFactory(key);
        _entries[key] = new WeakReference<TValue>(createdValue);
        return createdValue;
    }

    public void RemoveCollectedEntries()
    {
        foreach ((TKey key, WeakReference<TValue> weakReference) in _entries)
        {
            if (!weakReference.TryGetTarget(out _))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }
}
```

Two practical notes here:

1. Collected entries can leave dead weak references behind, so occasional cleanup helps.
2. You still need to think about concurrency and duplicate creation if multiple callers race to populate the same key.

That's why I usually keep this pattern small and boring. Once a cache needs strict eviction policies, size limits, or metrics, I reach for `IMemoryCache` instead.

## When I reach for `WeakReference<T>`

For me, the sweet spot looks like this:

- the value is expensive enough to reuse
- the value is safe and reasonably cheap to rebuild
- it's okay if the GC clears it at any time
- I want memory pressure to win over cache retention

If those aren't true, I probably want a normal cache with explicit policy instead.

## Final thought

`WeakReference<T>` is one of those features that's easy to overcomplicate. The simple version is usually the right one: keep a soft handle to derived data, try to reuse it, and be completely fine with rebuilding it.

That's not a replacement for a real cache. It's a lightweight way to say, "reuse this if memory allows."
