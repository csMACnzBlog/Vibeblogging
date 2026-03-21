---
title: "Zero-Allocation C# with Span and Memory"
date: 2026-03-21
tags: dotnet, csharp, performance, language-features
image: span-and-memory-in-csharp.png
---

Every allocation costs you something. It's a tiny tax on the heap, but those taxes add up — GC pressure, pause times, and throughput that never quite hits where you want it. For a long time in C#, working with slices of data meant creating substrings, new arrays, or intermediate buffers. That's changed with `Span\<T\>` and `Memory\<T\>`.

## The Problem With Substrings

Imagine you're parsing an HTTP header line like `Content-Type: application/json`. The classic approach reaches for `Split` or `Substring`, both of which allocate a new string for every piece you pull out. When you're doing this thousands of times a second, it starts to matter.

`Span\<T\>` lets you work with a *view* into existing memory — no copy, no new heap object.

## What is Span\<T\>

`Span\<T\>` is a stack-allocated struct that represents a contiguous region of memory. It can point into an array, a string, a `stackalloc` buffer, or unmanaged memory. Because it lives on the stack and never owns the memory it describes, it's extremely lightweight.

The key constraint is that `Span\<T\>` is a `ref struct`, which means it can't be boxed, stored on the heap, or used across async boundaries. That's the trade-off you accept for zero-allocation slicing.

## Basic Span\<T\> Usage

Creating a `Span\<int\>` from an array is straightforward:

```csharp
int[] numbers = [1, 2, 3, 4, 5, 6, 7, 8];

Span<int> all = numbers;           // implicit conversion
Span<int> middle = all.Slice(2, 4); // elements at index 2, 3, 4, 5

foreach (var n in middle)
    Console.WriteLine(n); // 3, 4, 5, 6
```

`Slice` returns a new `Span\<T\>` — it's just a pointer and a length, no data is copied. You can also use range syntax:

```csharp
Span<int> first3 = all[..3];
Span<int> last3  = all[^3..];
```

## ReadOnlySpan\<T\> for Strings

For string data, you'll use `ReadOnlySpan\<char\>`. Strings in .NET are immutable, so you can't write to them, but you can read from them without copying.

Here's a simple parser that extracts the value from a `key: value` header line:

```csharp
static ReadOnlySpan<char> ParseHeaderValue(ReadOnlySpan<char> line)
{
    int colonIndex = line.IndexOf(':');
    if (colonIndex < 0)
        return ReadOnlySpan<char>.Empty;

    return line.Slice(colonIndex + 1).TrimStart();
}

// Usage — no string allocations
string header = "Content-Type: application/json";
ReadOnlySpan<char> value = ParseHeaderValue(header);
Console.WriteLine(value.ToString()); // "application/json"
```

Calling `.ToString()` at the end does allocate — but only once, at the point you actually need a `string`. Everything in between is allocation-free.

## Stackalloc With Span\<T\>

For small temporary buffers, `stackalloc` gives you heap-free working space. Before `Span\<T\>` you had to use `unsafe` code to work with stackalloc memory. Now it's clean:

```csharp
Span<byte> buffer = stackalloc byte[256];
buffer.Clear();

// use buffer for some temporary work
int bytesWritten = Encoding.UTF8.GetBytes("hello world", buffer);
Console.WriteLine($"Wrote {bytesWritten} bytes to the stack buffer");
```

Keep stackalloc small — the stack is limited. A few hundred bytes is fine; kilobytes starts to get risky.

## Memory\<T\> — The Async-Safe Alternative

`Span\<T\>` can't cross `await` boundaries. The moment you have an async method, the compiler needs to store local state on the heap, and `ref struct` types can't participate in that. That's where `Memory\<T\>` comes in.

`Memory\<T\>` is a regular struct (not `ref struct`) that wraps the same concept — a slice of contiguous memory — but it can be stored as a field, passed to async methods, and used anywhere:

```csharp
class CsvParser
{
    private readonly Memory<char> _buffer;

    public CsvParser(char[] data)
    {
        _buffer = data;
    }

    public async Task<string[]> ParseLineAsync(int lineStart, int lineLength)
    {
        await Task.Yield(); // simulate async work

        // Get a Span<char> only when you need it for synchronous processing
        ReadOnlySpan<char> line = _buffer.Slice(lineStart, lineLength).Span;
        return line.ToString().Split(',');
    }
}
```

The pattern is: store `Memory\<T\>` as a field or pass it through async code; call `.Span` when you're back in synchronous context and need to do the actual processing.

## A Practical Parsing Example

Here's a zero-allocation CSV field extractor that processes a line without creating intermediate strings:

```csharp
static void PrintFields(ReadOnlySpan<char> line)
{
    while (!line.IsEmpty)
    {
        int comma = line.IndexOf(',');
        ReadOnlySpan<char> field = comma >= 0
            ? line[..comma]
            : line;

        Console.WriteLine(field.ToString());

        line = comma >= 0 ? line[(comma + 1)..] : ReadOnlySpan<char>.Empty;
    }
}

// Call it directly from a string — no allocations until Console.WriteLine
PrintFields("alice,30,engineer");
```

In a hot path that processes millions of rows, avoiding those intermediate string allocations makes a measurable difference.

## When to Reach for Each Type

**Use `Span\<T\>`** when you're doing synchronous work and don't need to store the slice anywhere — parsing, transforming, or inspecting data in a single method. It's the lowest-overhead option.

**Use `Memory\<T\>`** when you need to store the slice as a field, pass it to an async method, or hand it off to something you don't fully control. You pay a tiny bit more for the flexibility.

**Use `ReadOnlySpan\<T\>` and `ReadOnlyMemory\<T\>`** when you don't need to write to the data — which is most of the time for parsing scenarios.

## Limitations to Know

- `Span\<T\>` can't be a class field — it's a `ref struct`
- `Span\<T\>` can't survive an `await` — use `Memory\<T\>` instead
- `Memory\<T\>` can't point to `stackalloc` memory (the stack frame won't be alive long enough)
- These types don't play well with LINQ — you'll need `foreach` or manual indexing

## Wrapping Up

`Span\<T\>` and `Memory\<T\>` won't replace strings and arrays everywhere, but for the hot paths in your code — parsers, serializers, network buffers, anything that slices and dices data at high frequency — they're the right tool. Start with `ReadOnlySpan\<char\>` the next time you'd normally reach for `Substring`, and see how far you can push the allocation-free zone before you actually need a string.
