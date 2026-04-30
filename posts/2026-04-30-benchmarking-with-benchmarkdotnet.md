---
title: Benchmarking C# Code with BenchmarkDotNet
date: 2026-04-30
tags: dotnet, csharp, performance, tutorial
image: benchmarking-with-benchmarkdotnet.png
---

Performance intuition is almost always wrong. You'll look at two implementations and be convinced one is faster, run a benchmark, and discover you had it backwards. That's not a failure of intelligence — it's just that CPUs, JIT compilers, and memory allocators do surprising things. The only way to know is to measure.

BenchmarkDotNet is the standard tool for benchmarking .NET code. It handles all the awkward parts: JIT warmup, multiple iterations, statistical analysis, and memory tracking. You write a class with some methods, add an attribute, and run it.

## Adding BenchmarkDotNet

Add the NuGet package to your project:

```bash
dotnet add package BenchmarkDotNet
```

Or add it directly to your `.csproj`:

```xml
<PackageReference Include="BenchmarkDotNet" Version="0.14.*" />
```

For benchmarking you'll want a dedicated console project — don't benchmark inside your production app. A simple project referencing your library under test is the right structure.

## Your First Benchmark

Here's a benchmark comparing three common approaches to string building:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text;

BenchmarkRunner.Run<StringBenchmarks>();

[MemoryDiagnoser]
public class StringBenchmarks
{
    private static readonly string[] Words =
        ["performance", "testing", "in", "csharp", "is", "great"];

    [Benchmark(Baseline = true)]
    public string Concatenation()
    {
        var result = "";
        foreach (var word in Words)
            result += word + " ";
        return result.TrimEnd();
    }

    [Benchmark]
    public string StringBuilder()
    {
        var sb = new StringBuilder();
        foreach (var word in Words)
            sb.Append(word).Append(' ');
        return sb.ToString().TrimEnd();
    }

    [Benchmark]
    public string StringJoin()
    {
        return string.Join(" ", Words);
    }
}
```

`[Benchmark]` marks the methods to measure. `Baseline = true` on one of them gives you ratio columns in the output — you'll see how much faster or slower each alternative is relative to the baseline. `[MemoryDiagnoser]` adds allocation columns, which is often as important as the timing.

## Running It (Release Mode Is Not Optional)

You must run benchmarks in Release configuration. Debug builds disable optimisations and produce completely misleading numbers:

```bash
dotnet run -c Release
```

BenchmarkDotNet will refuse to run in Debug mode by default and print a big warning. It's protecting you from yourself.

When it runs, you'll see output something like this:

```
| Method        | Mean      | Ratio | Allocated |
|-------------- |----------:|------:|----------:|
| Concatenation | 312.4 ns  |  1.00 |     608 B |
| StringBuilder |  98.7 ns  |  0.32 |     192 B |
| StringJoin    |  61.2 ns  |  0.20 |     128 B |
```

Read this as: `string.Join` is about 5× faster and allocates 5× less memory than repeated `+=`. `StringBuilder` is in between. The choice for small, known-length arrays isn't even `StringBuilder` — it's `string.Join`.

## Parameterising Your Benchmarks

Real performance characteristics often depend on input size. `[Params]` lets you run the same benchmark across multiple values automatically:

```csharp
[MemoryDiagnoser]
public class SearchBenchmarks
{
    private List<int> _data = default!;

    [Params(100, 1_000, 10_000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = Enumerable.Range(0, Size).ToList();
    }

    [Benchmark(Baseline = true)]
    public bool LinearSearch()
    {
        return _data.Contains(Size / 2);
    }

    [Benchmark]
    public bool BinarySearch()
    {
        return _data.BinarySearch(Size / 2) >= 0;
    }
}
```

`[GlobalSetup]` runs once before the benchmark iterations start — it's where you initialise expensive state that isn't part of what you're measuring. Use it to build test data, load files, or populate caches.

`[IterationSetup]` is also available when you need fresh state before each individual iteration, though it's less common.

With `[Params]`, BenchmarkDotNet generates a result row for every combination of parameter values. You'll see how both approaches scale as the input grows — and for a pre-sorted list, binary search's advantage becomes more pronounced at larger sizes.

## Memory Diagnostics in Practice

`[MemoryDiagnoser]` is worth adding to almost every benchmark. Allocations matter because they drive GC pressure. A method that's fast but allocates constantly will hurt you in a sustained workload in ways the nanosecond timing won't reveal.

Here's a comparison that makes this concrete — parsing a date string with two approaches:

```csharp
[MemoryDiagnoser]
public class DateParseBenchmarks
{
    private const string Input = "2026-04-30";

    [Benchmark(Baseline = true)]
    public DateTime ParseExact()
    {
        return DateTime.ParseExact(Input, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    [Benchmark]
    public bool TryParseExact()
    {
        return DateTime.TryParseExact(Input, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _);
    }

    [Benchmark]
    public DateOnly ParseDateOnly()
    {
        return DateOnly.Parse(Input,
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
```

The allocation column often surprises people. You'll find `TryParseExact` avoids exception-path allocations and `DateOnly.Parse` can outperform `DateTime.ParseExact` for date-only scenarios. Without the numbers in front of you, you'd be guessing.

## A Practical Comparison: Filtering Collections

Here's a more realistic example — filtering a list of records with LINQ versus a hand-rolled loop:

```csharp
public record Order(int Id, string Status, decimal Amount);

[MemoryDiagnoser]
public class FilterBenchmarks
{
    private Order[] _orders = default!;

    [Params(1_000, 50_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var statuses = new[] { "Pending", "Shipped", "Cancelled" };
        var rng = new Random(42);
        _orders = Enumerable.Range(1, Count)
            .Select(i => new Order(
                i,
                statuses[rng.Next(statuses.Length)],
                rng.Next(10, 1000)))
            .ToArray();
    }

    [Benchmark(Baseline = true)]
    public decimal LinqFilter()
    {
        return _orders
            .Where(o => o.Status == "Shipped" && o.Amount > 100)
            .Sum(o => o.Amount);
    }

    [Benchmark]
    public decimal ManualLoop()
    {
        decimal total = 0;
        foreach (var order in _orders)
        {
            if (order.Status == "Shipped" && order.Amount > 100)
                total += order.Amount;
        }
        return total;
    }
}
```

In practice the manual loop is faster and allocates nothing (no iterator, no closure) while LINQ allocates an `IEnumerable` pipeline. Whether that matters depends entirely on how often this runs and on what data size. That's exactly what the benchmark tells you.

## When to Benchmark (and When to Stop)

Benchmarking is for when you have an identified hotspot. Profile first with a tool like dotnet-trace or Visual Studio's profiler to find where your application is actually spending time. Then write a benchmark to evaluate specific changes in that area.

Don't benchmark speculatively. Writing benchmarks for code that runs rarely or that isn't in any measured bottleneck is a way to spend hours optimising something that contributes 0.01% of your total runtime.

The other trap is over-optimising. Once you've confirmed a change is meaningfully faster and the code is still readable, stop. Shaving another 5 ns from an already-fast method while making it harder to maintain is a bad trade.

BenchmarkDotNet's output also includes confidence intervals and outlier counts. If the numbers look noisy or the ratio is 1.0 ± 0.3, the difference isn't real — move on.

## Wrapping Up

BenchmarkDotNet removes the excuses for guessing. It's straightforward to add, the output is clear, and the combination of timing plus allocation data gives you a full picture of what a change actually costs.

The workflow is the same every time: identify a hotspot, write a benchmark, try your alternatives, read the numbers, make a decision. You'll be wrong about performance less often, and when you optimise something, you'll know exactly why it's faster.

Add `[MemoryDiagnoser]` by default. Run in Release. Trust the numbers over the intuition.
