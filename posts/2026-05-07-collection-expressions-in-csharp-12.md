---
title: Collection Expressions in C# 12
date: 2026-05-07
tags: csharp, dotnet, tutorial
image: collection-expressions-in-csharp-12.png
---

C# 12 introduced collection expressions — a unified, concise syntax for initialising lists, arrays, spans, and any collection type that supports it. If you've ever thought "there are too many ways to write a list literal in C#", collection expressions are the answer.

## The Old Ways

Before C# 12, creating a collection depended entirely on its type:

```csharp
// Array
int[] numbers = new int[] { 1, 2, 3 };
int[] numbers = new[] { 1, 2, 3 };   // short form

// List
List<int> numbers = new List<int> { 1, 2, 3 };
List<int> numbers = new() { 1, 2, 3 };          // target-typed new

// ImmutableArray
ImmutableArray<int> numbers = ImmutableArray.Create(1, 2, 3);
```

Each type has its own syntax and factory method. They're all fine, but there's no single consistent way to say "here's a collection of values".

## Collection Expressions

C# 12 introduces the `[...]` syntax, which works the same way regardless of the target type:

```csharp
int[] array = [1, 2, 3];
List<int> list = [1, 2, 3];
Span<int> span = [1, 2, 3];
ImmutableArray<int> immutable = [1, 2, 3];
```

The compiler figures out how to construct each type from the target type annotation. The syntax is the same; only the left-hand side changes.

## The Spread Operator

The really useful part of collection expressions is the spread element `..`, which lets you inline the contents of another collection:

```csharp
int[] first = [1, 2, 3];
int[] second = [4, 5, 6];
int[] combined = [..first, ..second];    // [1, 2, 3, 4, 5, 6]
```

You can mix spread elements with literal values in any order:

```csharp
int[] withBookends = [0, ..first, ..second, 7];  // [0, 1, 2, 3, 4, 5, 6, 7]
```

This replaces the clunky `Concat` + `ToArray()` dance you'd have written before:

```csharp
// Old way
int[] combined = first.Concat(second).ToArray();

// New way
int[] combined = [..first, ..second];
```

The spread works with any type that implements `IEnumerable<T>`, so you can spread lists, arrays, spans, and any other enumerable.

## Empty Collections

Empty collection expressions are particularly clean. Instead of `new List<string>()` or `Array.Empty<string>()`, you just write:

```csharp
List<string> tags = [];
string[] names = [];
```

The compiler generates the most efficient representation for each type — for arrays, it actually uses `Array.Empty<T>()` under the hood.

## Working with Spans

Collection expressions have a special relationship with `Span<T>` and `ReadOnlySpan<T>`. The compiler can create stack-allocated spans from literal collection expressions, which is great for performance-sensitive code:

```csharp
ReadOnlySpan<char> vowels = ['a', 'e', 'i', 'o', 'u'];

// In a method that accepts ReadOnlySpan<byte>
ProcessData([0x01, 0x02, 0xFF]);
```

Previously, passing a literal array to a method expecting `Span<T>` required an intermediate allocation. With collection expressions, the compiler can skip the heap allocation entirely when the span doesn't escape.

## Inline in Method Arguments

Collection expressions work in any expression context where a collection type is expected, including method arguments:

```csharp
public void Register(IEnumerable<string> roles) { ... }

// Old way
Register(new[] { "admin", "user" });
Register(new List<string> { "admin", "user" });

// New way
Register(["admin", "user"]);
```

This is particularly handy in tests where you're passing collections to constructors or setup methods:

```csharp
var service = new NotificationService(
    handlers: [new EmailHandler(), new SmsHandler()],
    tags: ["alert", "transactional"]);
```

## Dictionary Expressions (A Sneak Preview)

C# 12 didn't include dictionary literals, but C# 13 adds them. In C# 13 you can write:

```csharp
// C# 13
Dictionary<string, int> scores = ["alice": 10, "bob": 7];
```

Collection expressions paved the way for this — the infrastructure to handle the `[...]` syntax and spread operator made it straightforward to extend to key-value pairs.

## Making Your Own Types Work

Any type can participate in collection expressions by implementing the `IEnumerable<T>` interface and including a `CollectionBuilderAttribute` pointing to a factory method:

```csharp
[CollectionBuilder(typeof(TagList), nameof(TagList.Create))]
public class TagList : IEnumerable<string>
{
    private readonly string[] _tags;

    private TagList(string[] tags) => _tags = tags;

    public static TagList Create(ReadOnlySpan<string> values)
        => new TagList(values.ToArray());

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_tags).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
```

Once that's in place, the collection expression syntax just works:

```csharp
TagList tags = ["dotnet", "csharp", "tutorial"];
TagList empty = [];
```

Most of the time you won't need to write this — the types you're working with already support it. But it's good to know the extension point exists.

## What Collection Expressions Replace

Here's a summary of the patterns collection expressions supersede:

```csharp
// Arrays
new int[] { 1, 2, 3 }              → [1, 2, 3]
new[] { 1, 2, 3 }                  → [1, 2, 3]
Array.Empty<int>()                  → []

// Lists
new List<int> { 1, 2, 3 }          → [1, 2, 3]
new() { 1, 2, 3 }                  → [1, 2, 3]
Enumerable.Empty<int>().ToList()    → []

// Concatenation
first.Concat(second).ToArray()      → [..first, ..second]
first.Concat(second).ToList()       → [..first, ..second]

// Prepend/Append
new[] { 0 }.Concat(items)          → [0, ..items]
items.Append(99)                    → [..items, 99]
```

Not every situation calls for a collection expression — if you need to build a collection dynamically in a loop, `List<T>.Add` is still the right tool. But for static initialisation and combining existing collections, the new syntax is almost always cleaner.

## Wrapping Up

Collection expressions in C# 12 unify the many ways to create collections into a single, readable syntax. The key things to remember:

- `[1, 2, 3]` works for arrays, lists, spans, and any collection that opts in
- `..spread` inlines another collection's contents inline
- `[]` gives you an empty collection — the compiler picks the best representation
- Method arguments and constructors accept collection expressions directly
- `Span<T>` benefits most — the compiler can avoid heap allocations for literals

It's one of those features that seems small until you're using it every day, and then you wonder how you lived without it.
