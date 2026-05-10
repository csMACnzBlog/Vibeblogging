---
title: params Collections in C# 13
date: 2026-05-10
tags: csharp, dotnet, tutorial
image: params-collections-in-csharp-13.png
---

If you've ever written a helper method that accepts a variable number of arguments and then immediately needed to pass the results to something that wants a `List<T>` or a `Span<T>`, C# 13's expanded `params` support cleans that right up. Previously `params` only worked with arrays — now it works with any collection type that the compiler knows how to construct.

## What Was Annoying Before

The old `params` was array-only. That meant an extra allocation or a conversion call the moment you needed to work with modern collection types:

```csharp
// Works, but you get T[]
public void LogAll(params string[] messages)
{
    foreach (var m in messages)
        Console.WriteLine(m);
}

// If you need ReadOnlySpan<T> or List<T>, you had to write overloads or call ToList()
public void LogAll(ReadOnlySpan<string> messages) { /* ... */ }
```

You could never write `params ReadOnlySpan<string>`. The compiler just didn't support it.

## C# 13 Lifts the Restriction

Now `params` works with any collection type that supports collection expressions — `Span<T>`, `ReadOnlySpan<T>`, `IEnumerable<T>`, `List<T>`, `ImmutableArray<T>`, and your own custom types too.

```csharp
public static void LogAll(params ReadOnlySpan<string> messages)
{
    foreach (var m in messages)
        Console.WriteLine(m);
}

// Call it exactly as before
LogAll("starting up", "loading config", "ready");
```

The compiler handles constructing the span from the call-site arguments, just like it did for arrays.

## Why `ReadOnlySpan<T>` Is the Interesting Case

`ReadOnlySpan<T>` is stack-allocated, so using it with `params` means you can accept a variable argument list with zero heap allocations in the common case. For hot paths — logging, assertion helpers, formatting utilities — that's a genuine win.

```csharp
public static double Average(params ReadOnlySpan<double> values)
{
    if (values.IsEmpty)
        return 0;

    double sum = 0;
    foreach (var v in values)
        sum += v;

    return sum / values.Length;
}

// No array created on the heap
double result = Average(1.5, 2.5, 3.0, 4.0);
```

Compare this to the array version: `params double[]` allocates a new array every time, even for a two-element call.

## Using `IEnumerable<T>` for Flexibility

If your API needs to be consumed by a wider variety of callers or used in LINQ chains, `IEnumerable<T>` works too:

```csharp
public static IEnumerable<string> Filtered(
    params IEnumerable<string> items)
{
    return items.Where(s => !string.IsNullOrWhiteSpace(s));
}

var cleaned = Filtered("  ", "hello", "", "world");
// ["hello", "world"]
```

It's not zero-allocation like `ReadOnlySpan<T>`, but it gives you the call-site convenience of `params` with the full power of LINQ on the receiving end.

## A Practical Validation Helper

Here's a pattern that shows up a lot in real code — guard clauses and validation utilities:

```csharp
using System.Runtime.CompilerServices;

public static class Validate
{
    public static void AllNotNull(
        params ReadOnlySpan<object?> values,
        [CallerArgumentExpression(nameof(values))] string? expression = null)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] is null)
            {
                throw new ArgumentNullException(
                    expression,
                    $"Argument at index {i} was null.");
            }
        }
    }
}

string? name = GetName();
string? email = GetEmail();

Validate.AllNotNull(name, email);
```

The combination of `params ReadOnlySpan<object?>` and `CallerArgumentExpression` (from [last time](callerargumentexpression-in-csharp-10.html)) gives you clean call sites and useful error messages with no extra allocation.

## Custom Collection Types

The feature also extends to any type that supports collection expression construction. If you've built a custom collection that implements the right pattern, it'll work:

```csharp
// A hypothetical validated collection type
[CollectionBuilder(typeof(ValidatedList), nameof(ValidatedList.Create))]
public class ValidatedList<T> : IEnumerable<T>
{
    private readonly List<T> _items;

    private ValidatedList(List<T> items) => _items = items;

    public static ValidatedList<T> Create(ReadOnlySpan<T> items)
        => new ValidatedList<T>(items.ToList());

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        => GetEnumerator();
}

public static void Process(params ValidatedList<int> numbers) { /* ... */ }

Process(1, 2, 3, 4); // Compiler builds a ValidatedList<int>
```

This is more niche, but it shows the feature is genuinely general-purpose.

## One Thing to Watch Out For

If you have an existing method with `params T[]` and you add an overload with `params ReadOnlySpan<T>`, the compiler prefers the `ReadOnlySpan<T>` overload at call sites where the arguments are supplied inline. That's usually what you want, but it can be surprising if you expected array semantics.

```csharp
public static void Demo(params string[] items)
    => Console.WriteLine("array: " + string.Join(", ", items));

public static void Demo(params ReadOnlySpan<string> items)
    => Console.WriteLine("span: " + string.Join(", ", items.ToArray()));

Demo("a", "b"); // prints: span: a, b
```

When in doubt, test which overload resolves with a quick debug call.

## Wrapping Up

`params Collections` is a small quality-of-life addition that removes a long-standing restriction without breaking anything:

- Use `params ReadOnlySpan<T>` for zero-allocation hot paths
- Use `params IEnumerable<T>` when you want LINQ compatibility
- Use `params List<T>` or `params ImmutableArray<T>` when callers already hold those types
- Combine with `CallerArgumentExpression` for self-documenting helpers

If your codebase has utility methods that currently juggle `params T[]` and then immediately call `.ToList()` or `.AsSpan()`, this is an easy upgrade.
