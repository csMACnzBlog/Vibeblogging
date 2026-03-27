---
title: Benchmarking with BenchmarkDotNet in C#
date: 2026-03-27
tags: dotnet, csharp, performance, benchmarking, optimization
image: benchmarking-with-benchmarkdotnet.png
---

You've just written two implementations of the same algorithm and you're wondering which one's faster. You could wrap them in `Stopwatch` calls and run them a few times, but that's surprisingly unreliable. Process warm-up, JIT compilation, garbage collection pauses — they all skew the numbers. That's where BenchmarkDotNet comes in.

BenchmarkDotNet is the go-to library for microbenchmarking in .NET. It handles warm-up, multiple iterations, statistical analysis, and even generates detailed reports. It's what the .NET team itself uses to measure runtime performance.

## Getting Started

Add BenchmarkDotNet to your project:

```bash
dotnet add package BenchmarkDotNet
```

Then write your first benchmark. Create a console app and add a benchmark class:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<StringBenchmarks>();

public class StringBenchmarks
{
    private const int Iterations = 1000;

    [Benchmark]
    public string Concatenation()
    {
        var result = "";
        for (int i = 0; i < Iterations; i++)
            result += i.ToString();
        return result;
    }

    [Benchmark]
    public string UsingStringBuilder()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Iterations; i++)
            sb.Append(i);
        return sb.ToString();
    }
}
```

Run it in Release mode — this is critical:

```bash
dotnet run --configuration Release
```

BenchmarkDotNet will run dozens of warm-up and actual iterations, then print a table showing mean time, standard deviation, and memory allocations. `UsingStringBuilder` will win by a wide margin, which confirms what we already knew — but now you have numbers.

## The [Params] Attribute

One of the most useful features is parameterized benchmarks. Instead of hardcoding `Iterations = 1000`, you can sweep across multiple values:

```csharp
public class StringBenchmarks
{
    [Params(100, 1000, 10_000)]
    public int Iterations { get; set; }

    [Benchmark]
    public string Concatenation()
    {
        var result = "";
        for (int i = 0; i < Iterations; i++)
            result += i.ToString();
        return result;
    }

    [Benchmark]
    public string UsingStringBuilder()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Iterations; i++)
            sb.Append(i);
        return sb.ToString();
    }
}
```

Now you get a row in the results table for each parameter value. This is great for spotting whether an algorithm's advantage grows or shrinks as the input scales.

## Measuring Memory with MemoryDiagnoser

Execution time tells only half the story. Allocations matter too — every allocation is a future GC pause. Add the `[MemoryDiagnoser]` attribute to your class:

```csharp
[MemoryDiagnoser]
public class StringBenchmarks
{
    // ... same methods as before
}
```

The results table now includes `Allocated` and `Gen0`/`Gen1`/`Gen2` GC collection counts. You'll often find that the "faster" method allocates more, which can hurt throughput under load due to GC pressure.

## Setup and Cleanup

If your benchmark needs state — say, a pre-populated list or a database connection — use `[GlobalSetup]` and `[GlobalCleanup]`:

```csharp
[MemoryDiagnoser]
public class ListSearchBenchmarks
{
    private List<int> _data = null!;
    private HashSet<int> _set = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _data = Enumerable.Range(0, 100_000).OrderBy(_ => rng.Next()).ToList();
        _set = new HashSet<int>(_data);
    }

    [Benchmark(Baseline = true)]
    public bool ListContains() => _data.Contains(50_000);

    [Benchmark]
    public bool HashSetContains() => _set.Contains(50_000);
}
```

Notice `Baseline = true` on `ListContains`. With a baseline set, the results table gains a `Ratio` column showing how many times faster or slower the other benchmarks are relative to it. At 100k elements, `HashSet.Contains` will be orders of magnitude faster — the ratio makes that obvious at a glance.

## Span vs Array: A Real-World Example

Here's a benchmark pattern that comes up constantly when optimizing hot paths — comparing array slicing with `Span<T>`:

```csharp
[MemoryDiagnoser]
public class SlicingBenchmarks
{
    private byte[] _buffer = null!;

    [GlobalSetup]
    public void Setup() => _buffer = new byte[4096];

    [Benchmark(Baseline = true)]
    public byte[] ArrayCopy()
    {
        var slice = new byte[256];
        Array.Copy(_buffer, 100, slice, 0, 256);
        return slice;
    }

    [Benchmark]
    public Span<byte> SpanSlice()
    {
        return _buffer.AsSpan(100, 256);
    }
}
```

`SpanSlice` will show zero allocations in the `Allocated` column — it's just a pointer and a length over the existing array. `ArrayCopy` allocates a new 256-byte array every call. In a tight loop processing network packets or file buffers, that difference adds up quickly.

## Choosing What to Benchmark

BenchmarkDotNet is powerful, but you should be selective. Don't benchmark everything — benchmark the hottest paths in your application. Profile first with tools like `dotnet-trace` or Visual Studio's profiler to find where time is actually spent, then write benchmarks to validate your optimizations.

A few rules of thumb:

- **Benchmark in Release mode.** Debug builds have no JIT optimizations and will give misleading numbers.
- **Avoid side-effect-free benchmarks.** BenchmarkDotNet is smart enough to detect and prevent the JIT from optimizing away your entire method, but make sure your benchmark returns a value or uses `[DoNotInline]` where needed.
- **Run on a quiet machine.** Background processes skew results. On CI, accept wider variance or disable noisy-neighbor processes.
- **Check the `StdDev`.** High standard deviation means your results are unreliable. Investigate outliers before drawing conclusions.

## Exporting Results

By default BenchmarkDotNet writes results to a `BenchmarkDotNet.Artifacts` folder in Markdown, HTML, and CSV formats. You can customize exporters with attributes:

```csharp
[HtmlExporter]
[CsvMeasurementsExporter]
[RPlotExporter]
[MemoryDiagnoser]
public class MyBenchmarks { ... }
```

The `RPlotExporter` generates bar charts if you have R installed — handy for putting performance comparisons in a report or PR description.

## Putting It All Together

Once you've integrated BenchmarkDotNet into your workflow, performance regressions become much harder to sneak in. You write a benchmark alongside your feature, commit the results to the repo, and run benchmarks again when something changes. If `Ratio` climbs, you investigate before merging.

Combined with Roslyn Analyzers (which we covered yesterday) you get two layers of quality enforcement: analyzers catch correctness and style issues at compile time, while BenchmarkDotNet keeps performance honest at runtime. Together they make a pretty solid feedback loop for writing fast, correct C# code.

Next up: Native AOT in .NET — where performance optimization goes all the way to the compilation model itself.
