---
title: Memoisation – Caching Expensive Computations
date: 2026-03-02
tags: csharp, performance, caching, memoisation, optimization
image: memoisation-caching-expensive-computations.png
---

We've been working through patterns that make your code more flexible and maintainable — [SOLID principles](solid-principles-foundation-of-good-design.html), the [Strategy Pattern](strategy-pattern-swapping-algorithms-at-runtime.html), [composition over inheritance](composition-over-inheritance-building-flexible-systems.html). Those are all about structure. Today we're talking about something a bit different: making your code faster by being lazy about doing work.

That's memoisation in a nutshell. Cache the result of an expensive computation the first time you run it, then return the cached result every time you're called with the same inputs again. Simple idea, surprisingly powerful in practice.

## The Problem: Doing the Same Work Over and Over

Let's start with a classic example — Fibonacci numbers. Calculating the nth Fibonacci number recursively is beautifully simple to write:

```csharp
public static long Fibonacci(int n)
{
    if (n <= 1) return n;
    return Fibonacci(n - 1) + Fibonacci(n - 2);
}
```

It reads almost like the mathematical definition. The problem is it's catastrophically slow. To calculate `Fibonacci(40)`, this function makes over 300 million calls. For `Fibonacci(50)` you're looking at over a trillion. That's because it recalculates the same values repeatedly — `Fibonacci(10)` gets computed thousands of times inside a single call to `Fibonacci(40)`.

Fibonacci is the textbook example, but the real-world version of this problem shows up constantly. You've got a method that fetches a user's permissions from the database. It's called dozens of times per request, but the permissions don't change during that request. Or you're calling an external pricing API to convert currencies, and you're hitting it for every line item in an order, even though the exchange rate is the same for all of them. Expensive work, repeated inputs, same results — wasted time.

That's exactly what memoisation is designed to fix.

## Simple Manual Memoisation with a Dictionary

The core mechanic is straightforward: check if you've already computed the answer for these inputs. If you have, return it. If not, compute it, store it, then return it.

```csharp
public static class FibonacciCalculator
{
    private static readonly Dictionary<int, long> _cache = new();

    public static long Fibonacci(int n)
    {
        if (n <= 1) return n;

        if (_cache.TryGetValue(n, out var cached))
            return cached;

        var result = Fibonacci(n - 1) + Fibonacci(n - 2);
        _cache[n] = result;
        return result;
    }
}
```

That's it. This version of `Fibonacci(40)` makes exactly 79 calls instead of 300 million. The first call warms the cache, every subsequent call with the same input hits the dictionary and returns immediately.

This works, but it's tied to a specific function. Every time you want to memoize something new, you're writing the same dictionary-check-and-store boilerplate again. Let's fix that.

## A Generic Memoisation Helper

What we really want is a reusable wrapper that takes any function and returns a memoised version of it. In C# we can do this cleanly with a higher-order function:

```csharp
public static class Memoize
{
    public static Func<TInput, TOutput> Of<TInput, TOutput>(
        Func<TInput, TOutput> func)
        where TInput : notnull
    {
        var cache = new Dictionary<TInput, TOutput>();

        return input =>
        {
            if (cache.TryGetValue(input, out var cached))
                return cached;

            var result = func(input);
            cache[input] = result;
            return result;
        };
    }
}
```

You wrap any function and get back a new function with caching baked in:

```csharp
Func<int, long> fibonacci = null!;
fibonacci = Memoize.Of<int, long>(n =>
{
    if (n <= 1) return n;
    return fibonacci(n - 1) + fibonacci(n - 2);
});

var result = fibonacci(40); // Fast, memoised recursion
```

You can also wrap lambdas for one-off cases without touching the original implementation:

```csharp
var expensiveOperation = Memoize.Of<string, decimal>(input =>
{
    // Simulate something slow
    Thread.Sleep(500);
    return input.Length * 1.5m;
});

var first  = expensiveOperation("hello"); // Takes ~500ms
var second = expensiveOperation("hello"); // Instant — from cache
var third  = expensiveOperation("world"); // Takes ~500ms — different input
```

### Handling Multiple Parameters

The single-input helper covers a lot of cases, but sometimes you need to memoize functions with multiple parameters. The cleanest approach is to wrap the inputs in a tuple:

```csharp
public static Func<T1, T2, TOutput> Of<T1, T2, TOutput>(
    Func<T1, T2, TOutput> func)
    where T1 : notnull
    where T2 : notnull
{
    var cache = new Dictionary<(T1, T2), TOutput>();

    return (a, b) =>
    {
        var key = (a, b);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var result = func(a, b);
        cache[key] = result;
        return result;
    };
}
```

C# tuples implement structural equality by default, so `(string, int)` tuples work as dictionary keys without any extra work on your part.

## A Real-World Example: Caching Database Lookups

Let's look at something more realistic than Fibonacci. Say you've got a product pricing service that frequently looks up tax rates by region:

```csharp
public class TaxRateService
{
    private readonly IDbConnection _db;

    public TaxRateService(IDbConnection db)
    {
        _db = db;
    }

    public decimal GetTaxRate(string regionCode)
    {
        // This hits the database every single time
        return _db.QuerySingle<decimal>(
            "SELECT Rate FROM TaxRates WHERE RegionCode = @code",
            new { code = regionCode });
    }
}
```

Tax rates don't change mid-request. If you're processing an order with 50 line items all from the same region, you're making 50 database round-trips for the same data. With memoisation:

```csharp
public class TaxRateService
{
    private readonly IDbConnection _db;
    private readonly Func<string, decimal> _getTaxRateMemoised;

    public TaxRateService(IDbConnection db)
    {
        _db = db;
        _getTaxRateMemoised = Memoize.Of<string, decimal>(FetchTaxRate);
    }

    public decimal GetTaxRate(string regionCode)
        => _getTaxRateMemoised(regionCode);

    private decimal FetchTaxRate(string regionCode)
    {
        return _db.QuerySingle<decimal>(
            "SELECT Rate FROM TaxRates WHERE RegionCode = @code",
            new { code = regionCode });
    }
}
```

Now the first call for `"GB"` hits the database. Every subsequent call returns the cached value instantly. The database doesn't know or care. From the caller's perspective, nothing changed — it's still just calling `GetTaxRate("GB")`.

### Caching API Calls

The same pattern works beautifully for external API calls. Here's a currency conversion service that you don't want hammering an external rate API on every call:

```csharp
public class CurrencyConverter
{
    private readonly HttpClient _httpClient;
    private readonly Func<(string From, string To), decimal> _getRate;

    public CurrencyConverter(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _getRate = Memoize.Of<(string From, string To), decimal>(
            pair => FetchExchangeRate(pair.From, pair.To));
    }

    public decimal Convert(decimal amount, string fromCurrency, string toCurrency)
    {
        var rate = _getRate((From: fromCurrency, To: toCurrency));
        return amount * rate;
    }

    private decimal FetchExchangeRate(string from, string to)
    {
        // Real implementation would call an external API
        var response = _httpClient
            .GetFromJsonAsync<ExchangeRateResponse>(
                $"https://api.exchangerates.example.com/latest?from={from}&to={to}")
            .GetAwaiter()
            .GetResult();

        return response!.Rate;
    }
}
```

In a single order processing run, you might call `Convert(price, "USD", "GBP")` hundreds of times. With the memoised rate lookup, the API is called once and the rest are cache hits.

## Thread Safety: Enter ConcurrentDictionary

The examples so far use a plain `Dictionary<TKey, TValue>`. That's fine for single-threaded scenarios, but if multiple threads are calling your memoised function simultaneously — which is very common in web apps — you've got a race condition.

Two threads could both check the cache, both find a miss, both compute the result, and both try to write it. With `Dictionary` that's undefined behaviour. With a bit more concurrency, you could end up with corrupted state.

The fix is to swap in `ConcurrentDictionary`:

```csharp
public static Func<TInput, TOutput> ThreadSafe<TInput, TOutput>(
    Func<TInput, TOutput> func)
    where TInput : notnull
{
    var cache = new ConcurrentDictionary<TInput, TOutput>();

    return input => cache.GetOrAdd(input, func);
}
```

`GetOrAdd` is atomic at the key level — it handles the check-then-set in a thread-safe way. The function might still be called more than once for the same key under heavy concurrency (two threads can both reach `GetOrAdd` before either has written a result), but the result stored will always be consistent, and you'll never corrupt the dictionary.

If calling the function more than once for the same input is genuinely problematic (say, it's got side effects, or it's very expensive), you need a bit more machinery:

```csharp
public static Func<TInput, TOutput> ThreadSafeStrict<TInput, TOutput>(
    Func<TInput, TOutput> func)
    where TInput : notnull
{
    var cache = new ConcurrentDictionary<TInput, Lazy<TOutput>>();

    return input =>
        cache.GetOrAdd(input, key => new Lazy<TOutput>(() => func(key))).Value;
}
```

Wrapping the result in `Lazy<T>` means only one thread will ever execute the factory function for a given key. Other threads waiting for the same key will block on `.Value` until the first thread finishes. It's a neat trick — you get the thread safety of `ConcurrentDictionary` combined with the single-execution guarantee of `Lazy<T>`.

## Cache Invalidation: The Hard Part

Phil Karlton famously said there are only two hard things in computer science: cache invalidation and naming things. He wasn't wrong.

Simple memoisation as we've built it so far has an infinite lifetime — entries never expire. That's fine for pure functions whose outputs never change (Fibonacci, mathematical conversions, etc.), but for anything backed by real-world data it's a problem.

### Time-Based Expiry (TTL)

A common approach is to attach a timestamp to each cache entry and treat it as stale after a certain duration:

```csharp
public class TimedMemoize<TInput, TOutput>
    where TInput : notnull
{
    private readonly Func<TInput, TOutput> _func;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<TInput, (TOutput Value, DateTime ExpiresAt)> _cache = new();

    public TimedMemoize(Func<TInput, TOutput> func, TimeSpan ttl)
    {
        _func = func;
        _ttl = ttl;
    }

    public TOutput Get(TInput input)
    {
        if (_cache.TryGetValue(input, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            return entry.Value;

        var result = _func(input);
        _cache[input] = (result, DateTime.UtcNow.Add(_ttl));
        return result;
    }
}
```

Usage looks like this:

```csharp
var taxRateLookup = new TimedMemoize<string, decimal>(
    regionCode => db.GetTaxRate(regionCode),
    ttl: TimeSpan.FromMinutes(15));

decimal rate = taxRateLookup.Get("GB"); // Fetched from DB
// 14 minutes later...
rate = taxRateLookup.Get("GB");         // Still from cache
// 16 minutes later...
rate = taxRateLookup.Get("GB");         // Cache expired, fetched from DB again
```

### Memory Pressure

The other thing to watch is unbounded growth. A dictionary that lives for the lifetime of the application will keep growing if you're caching results for a large or infinite input space. A naive memoisation of a method that takes arbitrary user IDs as input would eventually cache a result for every user in your system.

For long-lived caches you should either:
- Cap the number of entries and evict old ones (LRU-style)
- Use weak references so the GC can reclaim entries under memory pressure
- Or just use a proper caching infrastructure (more on that below)

## Memoisation vs IMemoryCache in ASP.NET Core

At this point you might be wondering: "why not just use `IMemoryCache`?" That's a completely fair question, and the honest answer is that for many scenarios in ASP.NET Core, you should.

`IMemoryCache` gives you:
- Built-in TTL and sliding expiration
- Memory pressure eviction (it integrates with the .NET memory pressure callbacks)
- Size limits
- Cache entry dependencies and callbacks
- Integration with the DI container

Here's the tax rate example using `IMemoryCache`:

```csharp
public class TaxRateService
{
    private readonly IDbConnection _db;
    private readonly IMemoryCache _cache;

    public TaxRateService(IDbConnection db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public decimal GetTaxRate(string regionCode)
    {
        return _cache.GetOrCreate($"taxrate:{regionCode}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return _db.QuerySingle<decimal>(
                "SELECT Rate FROM TaxRates WHERE RegionCode = @code",
                new { code = regionCode });
        })!;
    }
}
```

So when do you use simple memoisation instead? A few situations:

**Short-lived, request-scoped caching.** If you want to cache database results just for the lifetime of a single request (and not across requests), a plain `Dictionary` in a request-scoped service is simpler than `IMemoryCache` with manual cache keys and expiry management.

**Pure utility functions.** If you've got a helper function that parses or transforms data and you want to cache its results transparently, wrapping it in a memoised function is cleaner than injecting `IMemoryCache` everywhere.

**Libraries and non-web code.** `IMemoryCache` is an ASP.NET Core abstraction. If you're writing a library or a console app, pulling in `Microsoft.Extensions.Caching.Memory` just to memoize a function is overkill.

Think of memoisation as lightweight, function-level caching. `IMemoryCache` is application-level, infrastructure-style caching. Both are valid tools for different contexts.

## When NOT to Use Memoisation

Memoisation is powerful but it's not always appropriate. Here's when to leave it out:

### Functions with Side Effects

Memoisation assumes that calling a function multiple times with the same input is equivalent to calling it once. If your function sends an email, writes to a database, or fires a webhook, caching the result means those side effects only happen once. That might not be what you want.

```csharp
// Don't memoize this — the audit log entry should be written every time
public void LogAccess(string userId, string resource)
{
    _auditDb.Insert(new AuditEntry(userId, resource, DateTime.UtcNow));
}
```

### Non-Deterministic Functions

If a function's output depends on something other than its inputs — the current time, a random number, external state that changes frequently — memoisation will give you stale, wrong answers.

```csharp
// Don't memoize this — it depends on the current time
public bool IsMarketOpen(string exchange)
{
    var now = DateTime.UtcNow;
    return now.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
        && now.TimeOfDay >= TimeSpan.FromHours(9)
        && now.TimeOfDay < TimeSpan.FromHours(17);
}
```

### Large or Unbounded Input Spaces

If you're memoizing a function that gets called with a huge variety of inputs — like a search query string, or a large document body — you'll end up with a cache that uses an enormous amount of memory and has a very low hit rate. The overhead of the cache outweighs the benefit.

### Short-Lived or One-Time Computations

If a function is only ever called once or twice, caching its result adds complexity with no benefit. Memoisation earns its keep when the same inputs recur frequently. If they don't, it's just overhead.

## Putting It All Together

Here's a more complete example showing memoisation applied sensibly in a real service — a product catalogue that needs to look up category metadata and apply regional pricing:

```csharp
public class ProductCatalogueService
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IPricingApi _pricingApi;

    private readonly Func<int, CategoryDto?> _getCategory;
    private readonly Func<(string, string), decimal> _getExchangeRate;

    public ProductCatalogueService(
        IProductRepository products,
        ICategoryRepository categories,
        IPricingApi pricingApi)
    {
        _products = products;
        _categories = categories;
        _pricingApi = pricingApi;

        // Memoize the expensive lookups
        _getCategory = Memoize.ThreadSafe<int, CategoryDto?>(
            id => _categories.GetById(id));

        _getExchangeRate = Memoize.ThreadSafe<(string, string), decimal>(
            pair => _pricingApi.GetRate(pair.Item1, pair.Item2));
    }

    public IEnumerable<ProductViewModel> GetProductsForRegion(
        IEnumerable<int> productIds,
        string currency)
    {
        return productIds.Select(id =>
        {
            var product = _products.GetById(id);
            var category = _getCategory(product.CategoryId); // Cached after first call per category
            var rate = _getExchangeRate(("USD", currency));  // Cached after first call per currency pair

            return new ProductViewModel
            {
                Id = product.Id,
                Name = product.Name,
                CategoryName = category?.Name ?? "Uncategorised",
                Price = Math.Round(product.BasePrice * rate, 2)
            };
        });
    }
}
```

If you've got 200 products across 10 categories and you're converting from USD to GBP, you'll make 200 product lookups, 10 category lookups (one per unique category, not one per product), and 1 exchange rate call. Without memoisation, you'd be making 200 category lookups and 200 exchange rate calls.

## Wrapping Up

Memoisation is one of those techniques that feels almost too simple to be useful, until you apply it to a real performance problem and watch the numbers drop. The core idea — cache the result, skip the work — is just a dictionary lookup and a store. But the impact can be dramatic.

The key things to take away:

- **Use it for pure or near-pure functions** where the same inputs genuinely produce the same output.
- **Use `ConcurrentDictionary` or the `Lazy<T>` pattern** when multiple threads are in play.
- **Add TTL** when the underlying data can change, and think about how stale is acceptable.
- **Reach for `IMemoryCache`** when you're in ASP.NET Core and need proper eviction, size limits, and lifecycle management.
- **Avoid it** when functions have side effects, depend on external mutable state, or are called with too wide a variety of inputs to get a good hit rate.

Done well, memoisation is invisible to your callers, requires minimal code, and can turn an O(n) database-hammering loop into a handful of cache hits. It's a small addition that earns its place in any C# developer's toolkit.
