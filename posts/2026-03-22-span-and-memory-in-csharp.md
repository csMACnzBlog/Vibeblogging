---
title: Span and Memory in C#
date: 2026-03-22
tags: csharp, dotnet, performance, language-features
image: span-and-memory-in-csharp.png
---

If you've ever wondered how .NET achieves near-zero-allocation performance for string parsing or buffer operations, the answer usually involves `Span\<T\>` and `Memory\<T\>`. These types let you work with slices of memory without copying data, and they're worth adding to your toolkit.

## The Problem With Slicing

Let's say you're parsing a comma-separated string. The traditional approach looks something like this:

```csharp
string csv = "alice,bob,charlie";
string[] parts = csv.Split(',');
foreach (string part in parts)
{
    Console.WriteLine(part);
}
```

This works fine, but `Split` allocates a new array *and* a new string for each element. If you're doing this inside a tight loop or processing large payloads, those allocations add up fast — and the GC has to clean them all up.

## Introducing Span\<T\>

`Span\<T\>` is a ref struct that represents a contiguous region of arbitrary memory. It could be a slice of an array, a stack-allocated buffer, or unmanaged memory. The key property: it never owns the memory itself, so it never triggers allocations on its own.

Here's the same parsing example, allocation-free:

```csharp
ReadOnlySpan<char> csv = "alice,bob,charlie";

int start = 0;
for (int i = 0; i <= csv.Length; i++)
{
    if (i == csv.Length || csv[i] == ',')
    {
        ReadOnlySpan<char> token = csv.Slice(start, i - start);
        Console.WriteLine(token.ToString());
        start = i + 1;
    }
}
```

No array, no string allocations for the tokens themselves (just the final `ToString()` calls). `ReadOnlySpan\<char\>` can wrap a string literal directly.

## Slicing Arrays

`Span\<T\>` shines when you need to pass a portion of an array to a method without copying it:

```csharp
int[] numbers = { 1, 2, 3, 4, 5, 6, 7, 8 };

// Process just the middle chunk
Span<int> middle = numbers.AsSpan(2, 4); // [3, 4, 5, 6]

foreach (int n in middle)
    Console.Write($"{n} ");
// Output: 3 4 5 6
```

Changes to `middle` affect the original array — it's a view, not a copy. That's both a feature and something to be mindful of.

```csharp
middle[0] = 99;
Console.WriteLine(numbers[2]); // 99
```

## Stack Allocation With stackalloc

One of `Span\<T\>`'s killer features is working with stack-allocated buffers via `stackalloc`. This avoids heap allocation entirely:

```csharp
Span<byte> buffer = stackalloc byte[256];

// Use buffer for temp work
buffer[0] = 0xFF;
buffer[1] = 0xFE;

// Pass to a method expecting Span<byte>
ProcessBuffer(buffer);
```

Stack allocation is fast and GC-free. Just keep the size reasonable — the stack is limited and blowing it out will crash your app.

## Why Span\<T\> Is a ref struct

`Span\<T\>` is a `ref struct`, which means it has some restrictions you'll bump into:

- It can't be stored in a field on a regular class or struct
- It can't be used as a generic type argument
- It can't be boxed or used across `await` boundaries

These constraints exist because `Span\<T\>` might point to stack memory, and the GC doesn't track stack memory. If you need to store a reference to memory across async calls or in a class field, that's where `Memory\<T\>` comes in.

## Introducing Memory\<T\>

`Memory\<T\>` is the async-friendly counterpart to `Span\<T\>`. It's a regular struct (not a `ref struct`), so it can be stored, passed around, and used across `await` points. The trade-off: it can only point to heap memory, not stack memory.

```csharp
public async Task ProcessChunkAsync(Memory<byte> buffer)
{
    // This is fine — Memory<T> crosses await boundaries
    await Task.Delay(10);

    Span<byte> span = buffer.Span;
    for (int i = 0; i < span.Length; i++)
        span[i] ^= 0xFF; // Flip all bits
}
```

You get the `Span\<T\>` view via the `.Span` property whenever you need to do the actual work.

## Converting Between Them

You'll often start with one and need the other. Here's how they relate:

```csharp
byte[] array = new byte[1024];

// Array → Span
Span<byte> span = array.AsSpan();
Span<byte> slice = array.AsSpan(100, 200);

// Array → Memory
Memory<byte> memory = array.AsMemory();
Memory<byte> memorySlice = array.AsMemory(100, 200);

// Memory → Span (only valid synchronously)
Span<byte> fromMemory = memory.Span;
```

The conversion from `Memory\<T\>` to `Span\<T\>` is cheap — just a property access.

## A Real-World Example: Parsing Numbers

Here's a practical example: parsing a list of integers from a string without allocating intermediate substrings:

```csharp
public static IEnumerable<int> ParseInts(ReadOnlySpan<char> input)
{
    int start = 0;
    for (int i = 0; i <= input.Length; i++)
    {
        if (i == input.Length || input[i] == ',')
        {
            ReadOnlySpan<char> token = input.Slice(start, i - start).Trim();
            if (int.TryParse(token, out int value))
                yield return value;
            start = i + 1;
        }
    }
}

// Usage
foreach (int n in ParseInts("10, 20, 30, 40"))
    Console.WriteLine(n);
```

No intermediate strings. No array from `Split`. The only allocations are the yielded integers, which are value types on the stack anyway.

## When to Reach for These Types

You don't need `Span\<T\>` everywhere. Reach for it when:

- You're in a hot path where allocations matter (high-throughput parsers, serialisation, network buffers)
- You're working with large arrays and want to pass slices without copying
- You're using `stackalloc` for small temp buffers

For everyday business logic — building a CRUD API, processing a handful of form fields, generating a report — the regular string and array APIs are just fine.

## The Broader Picture

`Span\<T\>` and `Memory\<T\>` are part of a broader set of low-allocation APIs in .NET. You'll see them throughout the framework:

- `System.IO.Pipelines` uses `ReadOnlySequence\<byte\>` and `Memory\<byte\>` for async I/O
- `System.Text.Json` uses `Span\<byte\>` internally for fast UTF-8 parsing
- `StreamReader` has overloads that accept `Memory\<char\>` instead of allocating strings
- `MemoryMarshal` lets you reinterpret memory as different types

Once you understand these types, a lot of the performance-oriented .NET APIs start making more sense.

## Summary

`Span\<T\>` gives you a zero-allocation view into memory — arrays, stack buffers, or unmanaged memory — with full slice and index support. `Memory\<T\>` gives you the same thing but usable across `await` boundaries and in class fields. Together they're the foundation of high-performance C# code without unsafe pointers or manual memory management.

Start with `Span\<T\>` when you want to avoid copying slices of arrays. Graduate to `Memory\<T\>` when you need to store or pass those views asynchronously. And use `stackalloc` when you need a small scratch buffer and want to keep the GC out of it entirely.
