---
title: Generic Math in .NET with INumber<T>
date: 2026-05-13
tags: dotnet, csharp, generics, math
image: generic-math-in-dotnet.png
---

Before .NET 7, writing generic code that worked across numeric types meant ugly workarounds or multiple overloads. You couldn't write a generic `Sum<T>` that handled both `int` and `double` — `T` just didn't know how to add things.

.NET 7 and C# 11 fixed that with Generic Math. Now you can write numeric algorithms once and have them work across any numeric type.

## The Problem Generic Math Solves

Here's the kind of code that used to be painful:

```csharp
// Before .NET 7: you'd need separate overloads for each type
public static int Sum(IEnumerable<int> values) { /* ... */ }
public static double Sum(IEnumerable<double> values) { /* ... */ }
public static decimal Sum(IEnumerable<decimal> values) { /* ... */ }
```

If you added a new numeric type, you'd add another overload. Classic duplication.

The root problem was that `T` had no way to say "I support arithmetic". You couldn't call `T.Zero` or `a + b` on a generic type parameter.

## Static Abstract Interface Members

The key enabler is **static abstract interface members**, a C# 11 feature. It lets interfaces declare static members that implementing types must provide:

```csharp
public interface IAddable<T>
{
    static abstract T Zero { get; }
    static abstract T operator +(T left, T right);
}
```

Types that implement this must supply those static members. The compiler can then call them through the interface constraint.

That's exactly how `INumber<T>` works — all the arithmetic, comparison, and conversion operations are surfaced as static abstract members.

## Your First Generic Math Method

With `INumber<T>` from `System.Numerics`, you can write:

```csharp
using System.Numerics;

public static T Sum<T>(IEnumerable<T> values) where T : INumber<T>
{
    T result = T.Zero;
    foreach (var value in values)
        result += value;
    return result;
}
```

That single method works for any built-in numeric type:

```csharp
Console.WriteLine(Sum(new[] { 1, 2, 3 }));          // 6
Console.WriteLine(Sum(new[] { 1.1, 2.2, 3.3 }));    // 6.6
Console.WriteLine(Sum(new[] { 1m, 2m, 3m }));        // 6
```

No overloads. No reflection. Just one method that compiles cleanly for every numeric type.

## A Practical Stats Helper

Here's a more complete example using `ReadOnlySpan<T>` for performance:

```csharp
using System.Numerics;

public static class Stats<T> where T : INumber<T>
{
    public static T Min(ReadOnlySpan<T> values)
    {
        T min = values[0];
        foreach (var v in values[1..])
            if (v < min) min = v;
        return min;
    }

    public static T Max(ReadOnlySpan<T> values)
    {
        T max = values[0];
        foreach (var v in values[1..])
            if (v > max) max = v;
        return max;
    }

    public static T Sum(ReadOnlySpan<T> values)
    {
        T total = T.Zero;
        foreach (var v in values)
            total += v;
        return total;
    }

    public static T Mean(ReadOnlySpan<T> values) =>
        Sum(values) / T.CreateChecked(values.Length);
}
```

It works for both integer and floating-point types:

```csharp
int[] scores = { 88, 92, 74, 95, 61 };
Console.WriteLine(Stats<int>.Min(scores));   // 61
Console.WriteLine(Stats<int>.Max(scores));   // 95
Console.WriteLine(Stats<int>.Mean(scores));  // 82

double[] temps = { 18.5, 21.0, 19.3, 24.1 };
Console.WriteLine(Stats<double>.Mean(temps)); // 20.725
```

## The Numeric Interface Hierarchy

`System.Numerics` ships a whole family of interfaces you can constrain against:

| Interface | Use it when you need |
|---|---|
| `INumber<T>` | General arithmetic (add, subtract, compare) |
| `IFloatingPoint<T>` | Trig, rounding, NaN checks |
| `IBinaryInteger<T>` | Bitwise ops, shifts |
| `ISignedNumber<T>` | Negative values |
| `IUnsignedNumber<T>` | Unsigned-only guarantees |

For most generic number crunching, `INumber<T>` is the right choice. Reach for the more specific interfaces when you genuinely need floating-point functions or bit manipulation.

## Converting Between Types

`T.CreateChecked(value)` converts a value into `T` and throws if it doesn't fit:

```csharp
int x = int.CreateChecked(42L);      // OK
int y = int.CreateChecked(long.MaxValue); // OverflowException
```

Two alternatives for softer semantics:

- `T.CreateSaturating(value)` — clamps to `T.MinValue` / `T.MaxValue` instead of throwing.
- `T.CreateTruncating(value)` — truncates bits, like a C-style cast.

The checked version is the safest default. Use saturating when overflow tolerance is part of your algorithm's contract.

## Testing Generic Math Methods

Testing is straightforward with xUnit's `[Theory]`:

```csharp
public class StatsTests
{
    [Theory]
    [InlineData(new int[] { 1, 2, 3, 4, 5 }, 3)]
    [InlineData(new int[] { 10, 20, 30 }, 20)]
    public void Mean_ReturnsCorrectResult_ForInts(int[] values, int expected)
    {
        var result = Stats<int>.Mean(values);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(new double[] { 1.0, 2.0, 3.0 }, 2.0)]
    [InlineData(new double[] { 0.0, 10.0 }, 5.0)]
    public void Mean_ReturnsCorrectResult_ForDoubles(double[] values, double expected)
    {
        var result = Stats<double>.Mean(values);
        Assert.Equal(expected, result, precision: 10);
    }
}
```

The same `Stats<T>` class covers both test cases. You don't need two test classes for two types.

## Wrapping Up

Generic Math removes the copy-paste tax on numeric algorithms. If you've got utility methods that duplicate logic across `int`, `long`, `float`, and `decimal`, it's worth pulling them into a single generic version constrained to `INumber<T>`.

The static abstract interface member mechanism is also a useful pattern in its own right. Once you've seen it in `INumber<T>`, you'll spot opportunities to use it in your own library code when you want to define contracts over static behaviour.
