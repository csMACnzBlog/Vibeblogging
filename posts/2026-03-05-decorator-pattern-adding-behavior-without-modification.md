---
title: Decorator Pattern - Adding Behavior Without Modification
date: 2026-03-05
tags: csharp, design-patterns, decorator-pattern, architecture
image: decorator-pattern-adding-behavior-without-modification.png
---

At the end of the [Repository Pattern post](repository-pattern-abstracting-data-access.html) I left you with a teaser: you can wrap a repository with a caching decorator, adding caching transparently without touching your business logic or your original repository implementation. If you were already thinking "I could add caching with just a DI registration change" — that's exactly what we're building today.

The Decorator Pattern is about adding behaviour to an object by wrapping it, rather than modifying it. Your original class doesn't change. Your business logic doesn't change. You just add a wrapper in front that intercepts the call, does its extra work, and then delegates to the real thing. You can stack multiple wrappers. You can add and remove them by changing a single DI registration.

It's one of those patterns that, once you see it, you start noticing it everywhere.

## The Problem: Cross-Cutting Concerns

Most applications have behaviour that cuts across many different classes — logging, caching, validation, timing, retry logic. The naive approach is to put that behaviour directly into each class:

```csharp
public class SqlOrderRepository : IOrderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SqlOrderRepository> _logger;
    private readonly IMemoryCache _cache;

    public async Task<Order?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Fetching order {OrderId}", id);

        var cacheKey = $"order:{id}";
        if (_cache.TryGetValue(cacheKey, out Order? cached))
        {
            _logger.LogInformation("Cache hit for order {OrderId}", id);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        using var connection = _connectionFactory.CreateConnection();
        var order = await connection.QuerySingleOrDefaultAsync<Order>(
            "SELECT * FROM Orders WHERE Id = @id", new { id });
        stopwatch.Stop();

        _logger.LogInformation("Fetched order {OrderId} in {ElapsedMs}ms", id, stopwatch.ElapsedMilliseconds);

        if (order != null)
            _cache.Set(cacheKey, order, TimeSpan.FromMinutes(5));

        return order;
    }
    
    // ... every other method has the same logging and caching scaffolding
}
```

This works, but it's a mess. `SqlOrderRepository` now has three jobs: talking to the database, caching results, and logging. Its constructor takes three dependencies. Every method is buried under the same boilerplate. If you want to change the caching strategy, you're digging through SQL-focused code to find it.

The Decorator Pattern solves this by separating each concern into its own class.

## The Caching Decorator

The key insight is that a decorator implements the same interface as the thing it wraps. `CachingOrderRepository` implements `IOrderRepository`, and it wraps another `IOrderRepository`. From the outside, they're indistinguishable.

```csharp
public class CachingOrderRepository : IOrderRepository
{
    private readonly IOrderRepository _inner;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public CachingOrderRepository(IOrderRepository inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        var cacheKey = $"order:{id}";

        if (_cache.TryGetValue(cacheKey, out Order? cached))
            return cached;

        var order = await _inner.GetByIdAsync(id);

        if (order != null)
            _cache.Set(cacheKey, order, CacheDuration);

        return order;
    }

    public async Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId)
    {
        var cacheKey = $"orders:customer:{customerId}";

        if (_cache.TryGetValue(cacheKey, out IEnumerable<Order>? cached))
            return cached!;

        var orders = await _inner.GetByCustomerIdAsync(customerId);
        _cache.Set(cacheKey, orders, CacheDuration);
        return orders;
    }

    public async Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId)
    {
        var cacheKey = $"orders:customer:{customerId}:completed";

        if (_cache.TryGetValue(cacheKey, out IEnumerable<Order>? cached))
            return cached!;

        var orders = await _inner.GetCompletedByCustomerIdAsync(customerId);
        _cache.Set(cacheKey, orders, CacheDuration);
        return orders;
    }

    public async Task AddAsync(Order order)
    {
        await _inner.AddAsync(order);
        // Invalidate customer cache on write
        _cache.Remove($"orders:customer:{order.CustomerId}");
        _cache.Remove($"orders:customer:{order.CustomerId}:completed");
    }

    public async Task UpdateAsync(Order order)
    {
        await _inner.UpdateAsync(order);
        _cache.Remove($"order:{order.Id}");
        _cache.Remove($"orders:customer:{order.CustomerId}");
        _cache.Remove($"orders:customer:{order.CustomerId}:completed");
    }

    public async Task DeleteAsync(int id)
    {
        var order = await _inner.GetByIdAsync(id);
        await _inner.DeleteAsync(id);
        if (order != null)
        {
            _cache.Remove($"order:{id}");
            _cache.Remove($"orders:customer:{order.CustomerId}");
            _cache.Remove($"orders:customer:{order.CustomerId}:completed");
        }
    }
}
```

`CachingOrderRepository` knows nothing about SQL. It doesn't import Dapper. It doesn't know if the inner repository talks to SQL Server, PostgreSQL, or a JSON file. It just checks the cache, delegates if it misses, and stores the result. That's its entire job.

`SqlOrderRepository` knows nothing about caching. It just executes queries. That's its entire job.

### Wiring It Up in DI

Here's the DI registration change I promised. Before the caching decorator:

```csharp
services.AddScoped<IOrderRepository, SqlOrderRepository>();
```

After:

```csharp
services.AddScoped<SqlOrderRepository>();
services.AddScoped<IOrderRepository>(sp =>
    new CachingOrderRepository(
        sp.GetRequiredService<SqlOrderRepository>(),
        sp.GetRequiredService<IMemoryCache>()));
```

That's it. `OrderService` receives an `IOrderRepository` and has no idea it's talking to a caching wrapper. If you want to remove the caching — maybe for testing, maybe for debugging — you change one line and go back to registering `SqlOrderRepository` directly.

## The Logging Decorator

Now let's add logging the same way. A separate decorator, wrapping the same interface:

```csharp
public class LoggingOrderRepository : IOrderRepository
{
    private readonly IOrderRepository _inner;
    private readonly ILogger<LoggingOrderRepository> _logger;

    public LoggingOrderRepository(IOrderRepository inner, ILogger<LoggingOrderRepository> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        _logger.LogDebug("Fetching order {OrderId}", id);
        var stopwatch = Stopwatch.StartNew();

        var order = await _inner.GetByIdAsync(id);

        stopwatch.Stop();
        _logger.LogDebug("Fetched order {OrderId} in {ElapsedMs}ms — {Result}",
            id, stopwatch.ElapsedMilliseconds, order != null ? "found" : "not found");

        return order;
    }

    public async Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId)
    {
        _logger.LogDebug("Fetching orders for customer {CustomerId}", customerId);
        var stopwatch = Stopwatch.StartNew();

        var orders = await _inner.GetByCustomerIdAsync(customerId);
        var orderList = orders.ToList();

        stopwatch.Stop();
        _logger.LogDebug("Fetched {Count} orders for customer {CustomerId} in {ElapsedMs}ms",
            orderList.Count, customerId, stopwatch.ElapsedMilliseconds);

        return orderList;
    }

    public async Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId)
    {
        _logger.LogDebug("Fetching completed orders for customer {CustomerId}", customerId);
        var orders = await _inner.GetCompletedByCustomerIdAsync(customerId);
        return orders;
    }

    public async Task AddAsync(Order order)
    {
        _logger.LogInformation("Adding order for customer {CustomerId}, total {Total}",
            order.CustomerId, order.Total);
        await _inner.AddAsync(order);
        _logger.LogInformation("Added order {OrderId}", order.Id);
    }

    public async Task UpdateAsync(Order order)
    {
        _logger.LogInformation("Updating order {OrderId}", order.Id);
        await _inner.UpdateAsync(order);
    }

    public async Task DeleteAsync(int id)
    {
        _logger.LogWarning("Deleting order {OrderId}", id);
        await _inner.DeleteAsync(id);
        _logger.LogInformation("Deleted order {OrderId}", id);
    }
}
```

Same shape. Same principle. `LoggingOrderRepository` delegates everything to `_inner` and wraps each call with timing and structured log entries.

## The Validation Decorator

The same pattern extends to validation. Instead of putting guard clauses inside `SqlOrderRepository` (or worse, duplicating them across multiple implementations), you create a validation decorator:

```csharp
public class ValidatingOrderRepository : IOrderRepository
{
    private readonly IOrderRepository _inner;

    public ValidatingOrderRepository(IOrderRepository inner)
    {
        _inner = inner;
    }

    public Task<Order?> GetByIdAsync(int id)
    {
        if (id <= 0)
            throw new ArgumentException("Order ID must be positive.", nameof(id));

        return _inner.GetByIdAsync(id);
    }

    public Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId)
    {
        if (customerId <= 0)
            throw new ArgumentException("Customer ID must be positive.", nameof(customerId));

        return _inner.GetByCustomerIdAsync(customerId);
    }

    public Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId)
    {
        if (customerId <= 0)
            throw new ArgumentException("Customer ID must be positive.", nameof(customerId));

        return _inner.GetCompletedByCustomerIdAsync(customerId);
    }

    public Task AddAsync(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.CustomerId <= 0)
            throw new ArgumentException("Order must have a valid customer.", nameof(order));

        if (order.Total < 0)
            throw new ArgumentException("Order total cannot be negative.", nameof(order));

        if (string.IsNullOrWhiteSpace(order.Status))
            throw new ArgumentException("Order must have a status.", nameof(order));

        return _inner.AddAsync(order);
    }

    public Task UpdateAsync(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Id <= 0)
            throw new ArgumentException("Order must have a valid ID to update.", nameof(order));

        return _inner.UpdateAsync(order);
    }

    public Task DeleteAsync(int id)
    {
        if (id <= 0)
            throw new ArgumentException("Order ID must be positive.", nameof(id));

        return _inner.DeleteAsync(id);
    }
}
```

Validation logic lives in one place. You can test it completely independently of any database. And when you write the validation tests, they don't need a mock — just a fake `IOrderRepository` implementation.

## Why Not Just Subclass?

Before we go further, it's worth asking the obvious question: why not just subclass `SqlOrderRepository` and override the methods?

```csharp
// Don't do this
public class CachingSqlOrderRepository : SqlOrderRepository
{
    private readonly IMemoryCache _cache;

    public CachingSqlOrderRepository(IDbConnectionFactory factory, IMemoryCache cache)
        : base(factory)
    {
        _cache = cache;
    }

    public override async Task<Order?> GetByIdAsync(int id)
    {
        var cacheKey = $"order:{id}";
        if (_cache.TryGetValue(cacheKey, out Order? cached))
            return cached;

        var order = await base.GetByIdAsync(id);
        if (order != null) _cache.Set(cacheKey, order, TimeSpan.FromMinutes(5));
        return order;
    }
}
```

This looks like it works, but it has some serious problems.

**You're tied to `SqlOrderRepository`**. The subclass can only wrap a SQL implementation. If you switch to `EfOrderRepository`, you'd need a separate `CachingEfOrderRepository`. The caching logic gets duplicated. And a `LoggingCachingSqlOrderRepository` would be its own third class. Each additional concern multiplies the number of subclasses.

**You can't compose behaviours freely**. With inheritance, you have to decide upfront which combinations exist. With decorators, you compose at registration time and can change the stack without touching any classes.

**The fragile base class problem**. If `SqlOrderRepository` adds a new method, you have to remember to override it in every subclass. Miss one and your subclass silently bypasses the cache for that method.

**You can't apply the same decorator to a different interface**. Your `CachingOrderRepository` wraps `IOrderRepository`. A decorator for `IProductRepository` would need its own caching class, but the structure is identical. That's where generic decorators (which we'll look at below) come in. With inheritance tied to a concrete class, you can't do that.

The decorator approach sidesteps all of this. Each concern is independent. Composing them is a DI configuration choice.

## Stacking Decorators

The real power of the Decorator Pattern emerges when you stack multiple decorators. Let's wire up all three we've built:

```csharp
services.AddMemoryCache();
services.AddScoped<SqlOrderRepository>();

services.AddScoped<IOrderRepository>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<LoggingOrderRepository>>();
    var cache = sp.GetRequiredService<IMemoryCache>();

    IOrderRepository repository = sp.GetRequiredService<SqlOrderRepository>();
    repository = new CachingOrderRepository(repository, cache);
    repository = new LoggingOrderRepository(repository, logger);
    repository = new ValidatingOrderRepository(repository);

    return repository;
});
```

When `OrderService` calls `GetByIdAsync(42)`, here's what happens:

1. `ValidatingOrderRepository` checks that `42 > 0` — passes
2. `LoggingOrderRepository` logs "Fetching order 42" and starts a timer
3. `CachingOrderRepository` checks the cache — miss, delegates down
4. `SqlOrderRepository` runs the query and returns the result
5. `CachingOrderRepository` stores the result in cache, returns it
6. `LoggingOrderRepository` logs "Fetched order 42 in 12ms — found", returns it
7. `ValidatingOrderRepository` returns it to `OrderService`

The second call to `GetByIdAsync(42)`:

1. `ValidatingOrderRepository` checks that `42 > 0` — passes
2. `LoggingOrderRepository` logs and starts timer
3. `CachingOrderRepository` checks the cache — **hit**, returns immediately
4. `LoggingOrderRepository` logs "Fetched order 42 in 0ms — found", returns it
5. `ValidatingOrderRepository` returns it to `OrderService`

The SQL repository is never called on the cache hit. The logging decorator still fires, so you can observe the cache behaviour in your logs. And the validation decorator is always first, so invalid inputs never even reach the cache.

You can reorder the layers to change the behaviour. Want logging to record cache hits separately from database hits? Put `LoggingOrderRepository` inside `CachingOrderRepository`. Want validation to skip on cache hits? Put `ValidatingOrderRepository` inside `CachingOrderRepository`. The composition is just code — you can change it without modifying any of the decorators themselves.

## You've Already Used This Pattern

If you've written LINQ queries in C#, you've used decorator chains. Every LINQ extension method returns an `IEnumerable<T>` that wraps the previous one:

```csharp
var result = orders
    .Where(o => o.Status == "Completed")    // returns a WhereEnumerable wrapping orders
    .Select(o => o.Total)                   // returns a SelectEnumerable wrapping that
    .OrderByDescending(t => t)              // returns an OrderedEnumerable wrapping that
    .Take(10);                              // returns a TakeEnumerable wrapping that
```

Nothing is computed until you iterate `result`. Each operator wraps the previous one, intercepts the enumeration, and transforms it. That's a decorator chain. The same principle applies when you stack `StreamReader` over `GZipStream` over `FileStream` in the BCL — each one wraps the previous and adds behaviour.

The Decorator Pattern isn't exotic. It's the foundation of some of the most familiar APIs in the .NET ecosystem.

## A Generic Decorator Approach

If you find yourself building many decorators for different interfaces with the same cross-cutting concern, a generic approach can reduce duplication. Here's a simplified timing decorator using `DispatchProxy`:

```csharp
public class TimingDecorator<T> : DispatchProxy where T : class
{
    private T _inner = null!;
    private ILogger _logger = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = targetMethod!.Invoke(_inner, args);
        stopwatch.Stop();

        _logger.LogDebug("{Interface}.{Method} completed in {ElapsedMs}ms",
            typeof(T).Name, targetMethod.Name, stopwatch.ElapsedMilliseconds);

        return result;
    }

    public static T Wrap(T inner, ILogger logger)
    {
        var proxy = Create<T, TimingDecorator<T>>();
        var decorator = (TimingDecorator<T>)(object)proxy;
        decorator._inner = inner;
        decorator._logger = logger;
        return proxy;
    }
}
```

Usage is clean:

```csharp
var repository = new SqlOrderRepository(connectionFactory);
var timedRepository = TimingDecorator<IOrderRepository>.Wrap(repository, logger);
```

`DispatchProxy` intercepts every method call, so you write the timing logic once and it applies to every method on the interface automatically. The trade-off is reflection overhead and loss of compile-time visibility. For most applications, explicit decorators are clearer. But if you've got a dozen interfaces and you need consistent timing across all of them, `DispatchProxy` is worth knowing about.

For production use at any scale, libraries like [Scrutor](https://github.com/khellang/Scrutor) provide decorator registration helpers that make the DI wiring much cleaner:

```csharp
services.AddScoped<IOrderRepository, SqlOrderRepository>();
services.Decorate<IOrderRepository, CachingOrderRepository>();
services.Decorate<IOrderRepository, LoggingOrderRepository>();
services.Decorate<IOrderRepository, ValidatingOrderRepository>();
```

Each `Decorate` call wraps the current registration. The last one registered becomes the outermost layer. It's the stacking logic from earlier, expressed as a clean sequence of calls.

## The Downsides

It's worth being honest about the downsides, because they're real.

**Interface proliferation**. The Decorator Pattern requires every wrapped class to implement the same interface. If you're working with a class that doesn't have an interface, you'll need to extract one first. That's usually worth doing anyway, but it's a real step.

**Method boilerplate**. Every decorator has to implement every method on the interface, even the ones it doesn't care about — those just delegate straight to `_inner`. For large interfaces this gets tedious. The `DispatchProxy` approach solves this, at the cost of clarity.

**Debugging complexity**. A stack of five decorators means a stack trace that goes through five classes before reaching the actual implementation. When something goes wrong, you need to know which layer the failure originated in. Good logging in each decorator helps, but it adds cognitive overhead.

**Not a replacement for correct design**. If your repository interface has 15 methods, writing a caching decorator that correctly handles cache invalidation for all of them is a significant task. Sometimes the right answer is to split the interface into smaller ones first.

## When to Use It

Use the Decorator Pattern when:

1. **You need to add behaviour without modifying the original class** — The original is well-tested, or it's in a library you don't own, or the Open/Closed Principle matters to you.
2. **The behaviour is cross-cutting** — Caching, logging, validation, timing, retry logic. Things that apply consistently around an operation rather than inside it.
3. **You want composable, optional behaviour** — Different environments or clients need different combinations of behaviours. Compose what you need at registration time.
4. **You're working against an interface** — If the thing you're wrapping is already an abstraction, adding a decorator is natural and cheap.

Skip it when:

1. **You only need the behaviour once** — If there's one class and one concern and you'll never reuse it, just put the logic in the class. The indirection isn't free.
2. **The interface is very large** — A 20-method interface means a 20-method decorator. Consider whether the interface should be split first.
3. **Performance is critical at the micro level** — Each decorator adds a method call and potentially a heap allocation. For most code this is negligible, but if you're in a tight loop with millions of iterations, that overhead adds up.
4. **The behaviour isn't truly additive** — If what you need fundamentally changes what the method does (not wraps it), that's not a decorator, that's a different implementation.

## Wrapping Up

The Decorator Pattern is about keeping concerns separate and composable. `SqlOrderRepository` handles SQL. `CachingOrderRepository` handles caching. `LoggingOrderRepository` handles logging. Each class has one job, and you assemble the combination you need at registration time with a DI configuration change.

We've now seen several patterns in this series that work beautifully together. The [Factory Pattern](factory-pattern-hiding-object-creation.html) creates the connection. The [Repository Pattern](repository-pattern-abstracting-data-access.html) hides where data comes from. The Decorator Pattern adds cross-cutting behaviour without touching either of those. Each pattern solves a different problem, and they compose cleanly without knowing about each other.

Up next in the series: the **Chain of Responsibility Pattern**. Where the Decorator Pattern stacks wrappers around a single object, Chain of Responsibility passes a request through a sequence of handlers, each deciding whether to handle it, modify it, or pass it on. You'll see how it applies to middleware pipelines, request processing, and anywhere you've got a series of conditional checks that need to stay flexible and independently testable.
