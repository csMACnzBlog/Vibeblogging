---
title: GeneratedRegex in .NET Fast Regex Patterns
date: 2026-06-09
tags: csharp, dotnet, performance, regex
image: generatedregex-in-dotnet-fast-regex-without-runtime-cost.png
---

If you're validating input with regular expressions in hot paths, it's easy to miss where the cost comes from. Most of us write `new Regex(...)`, move on, and assume the runtime will sort it out.

It works, but it also means parsing and building regex internals at runtime. In .NET, `GeneratedRegex` lets you move that work to compile time, so your app starts faster and spends less CPU doing setup.

## The runtime regex pattern most of us start with

Here's a totally normal approach for validating an order reference like `ORD-123456`:

```csharp
using System.Text.RegularExpressions;

public static class OrderReferenceValidator
{
    private static readonly Regex OrderReferenceRegex =
        new("^ORD-[0-9]{6}$", RegexOptions.Compiled);

    public static bool IsValid(string value)
    {
        return OrderReferenceRegex.IsMatch(value);
    }
}
```

This is already better than creating a new `Regex` for every call. But the pattern still gets interpreted at runtime, and `RegexOptions.Compiled` can increase startup overhead.

## Switching to `GeneratedRegex`

With source generation, you define a partial method and let the compiler generate the regex implementation:

```csharp
using System.Text.RegularExpressions;

public static partial class OrderReferenceValidator
{
    [GeneratedRegex("^ORD-[0-9]{6}$")]
    private static partial Regex OrderReferenceRegex();

    public static bool IsValid(string value)
    {
        return OrderReferenceRegex().IsMatch(value);
    }
}
```

That's the whole trick. You keep the same matching behavior, but the regex machinery is generated at build time.

## Add options and timeout explicitly

For production services, it's worth being explicit about case sensitivity and timeouts:

```csharp
using System;
using System.Text.RegularExpressions;

public static partial class ProductCodeValidator
{
    [GeneratedRegex(
        "^[A-Z]{3}-[A-Z0-9]{4}$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex ProductCodeRegex();

    public static bool IsValid(string value)
    {
        return ProductCodeRegex().IsMatch(value);
    }
}
```

A bounded timeout helps guard against catastrophic backtracking when patterns get more complex over time.

## A practical parsing example

Validation is great, but extraction is usually where regex earns its keep. Let's parse a tiny log line format:

```csharp
using System;
using System.Globalization;
using System.Text.RegularExpressions;

public sealed record ApiLogEntry(DateTime Timestamp, string Level, string Route, int StatusCode);

public static partial class ApiLogParser
{
    [GeneratedRegex(
        "^(?<ts>\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z) (?<level>[A-Z]+) (?<route>/\\S+) (?<status>\\d{3})$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex EntryRegex();

    public static bool TryParse(string line, out ApiLogEntry? entry)
    {
        var match = EntryRegex().Match(line);
        if (!match.Success)
        {
            entry = null;
            return false;
        }

        var timestamp = DateTime.ParseExact(
            match.Groups["ts"].Value,
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        var level = match.Groups["level"].Value;
        var route = match.Groups["route"].Value;
        var statusCode = int.Parse(match.Groups["status"].Value, CultureInfo.InvariantCulture);

        entry = new ApiLogEntry(timestamp, level, route, statusCode);
        return true;
    }
}
```

That gives you strict shape validation and typed output in one pass.

## When `GeneratedRegex` is a great fit

You'll usually get the most benefit when:

- The same pattern is used a lot
- The regex lives in a library or service that values startup time
- You want compile-time generation rather than runtime parsing overhead

If your pattern is dynamic (user-provided, config-driven, or assembled at runtime), stick with normal `Regex` creation.

## One small gotcha to remember

`GeneratedRegex` uses a partial method in a partial type. If either `partial` keyword is missing, the build fails. It's a simple rule, but it's the first thing to check when you hit confusing compile errors.

## Final thought

`GeneratedRegex` isn't flashy, but it's a tidy upgrade for regex-heavy code. You keep expressive patterns, reduce runtime setup work, and keep your validation/parsing logic easy to read.

If you've got a few core patterns running on every request, this is one of those changes that's small to implement and quietly excellent in production.
