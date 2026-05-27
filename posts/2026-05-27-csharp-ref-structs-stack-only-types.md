---
title: C# Ref Structs & Stack-Only Types
date: 2026-05-27
tags: csharp, dotnet, performance, memory
image: csharp-ref-structs-stack-only-types.png
---

I used to hear “stack-only type” and immediately assume it was some niche trick for people benchmarking everything down to the nanosecond.

Turns out, `ref struct` and `Span<T>` are useful way earlier than that. If you parse text, slice buffers, or transform data in tight loops, stack-only types can remove a lot of allocation noise without making your code unreadable.

Let’s walk through what they are, why the compiler is strict about them, and how to use them without fighting the language.

## Why stack-only types exist

The short version: `Span<T>` points at memory that might not be safe to keep around forever. It could refer to stack memory, pooled buffers, or native memory.

So C# gives us `ref struct` to represent values that must stay on the stack and must not escape to places where lifetime guarantees break.

That’s why you can’t:

- store a `Span<T>` in a class field
- capture it in a lambda
- use it across `await`
- box it as `object`

Those constraints feel annoying at first, but they’re exactly what keeps these types safe.

## A small `ref struct` parser

Here’s a tiny CSV-like parser that reads two comma-separated integers without allocating substrings:

```csharp
using System;

public readonly ref struct IntPairParser(ReadOnlySpan<char> input)
{
    private readonly ReadOnlySpan<char> _input = input;

    public bool TryParse(out int first, out int second)
    {
        first = 0;
        second = 0;

        int comma = _input.IndexOf(',');
        if (comma < 0)
        {
            return false;
        }

        ReadOnlySpan<char> left = _input[..comma].Trim();
        ReadOnlySpan<char> right = _input[(comma + 1)..].Trim();

        return int.TryParse(left, out first) && int.TryParse(right, out second);
    }
}

var parser = new IntPairParser(" 42, 99 ".AsSpan());
if (parser.TryParse(out var a, out var b))
{
    Console.WriteLine($"Parsed: {a} and {b}");
}
```

No `Split`, no temporary string array, and no substring allocations. Just slices over the original buffer.

## `stackalloc` without drama

`stackalloc` lets you allocate small buffers directly on the stack. It’s great for short-lived scratch space.

```csharp
using System;

Span<byte> temp = stackalloc byte[32];
for (int i = 0; i < temp.Length; i++)
{
    temp[i] = (byte)i;
}

int checksum = 0;
foreach (byte b in temp)
{
    checksum += b;
}

Console.WriteLine($"Checksum: {checksum}");
```

I try to keep `stackalloc` buffers modest in size and scoped tightly. If you need bigger or long-lived buffers, pooled arrays are usually a better fit.

## A practical pattern: parse in sync, process async later

One of the most common gotchas is trying to carry a `Span<T>` into async code. That won’t compile, and that’s by design.

The pattern that works is:

1. parse synchronously with spans
2. convert to stable data (`string`, `record`, etc.)
3. continue asynchronously

```csharp
using System;
using System.Threading.Tasks;

public static class MessagePipeline
{
    public static async Task HandleAsync(string raw)
    {
        ReadOnlySpan<char> span = raw.AsSpan();
        int separator = span.IndexOf('|');
        if (separator < 0)
        {
            return;
        }

        string type = span[..separator].ToString();
        string payload = span[(separator + 1)..].ToString();

        await PersistAsync(type, payload);
    }

    private static Task PersistAsync(string type, string payload)
    {
        Console.WriteLine($"Type={type}, Payload={payload}");
        return Task.CompletedTask;
    }
}
```

That gives you fast parsing where it matters, then safe async flow with regular managed types.

## Rules of thumb I use

When I’m deciding whether stack-only types are worth it, I keep it simple:

- use `ReadOnlySpan<char>` for parsing/slicing input text
- keep span-heavy logic in small synchronous methods
- convert to stable types at boundaries (async, queues, persistence)
- prefer clarity over cleverness
- measure before and after if performance is the reason

`ref struct` is not about writing “hardcore” C#. It’s mostly about making memory lifetime explicit and letting the compiler keep you honest.

## Final thought

If you’ve avoided `ref struct` because the rules looked intimidating, you’re not alone. I did too.

But once you treat it as a focused tool for parsing and short-lived transformations, it clicks quickly. You get less allocation churn, predictable lifetimes, and code that still feels straightforward.

That’s a pretty good trade.
