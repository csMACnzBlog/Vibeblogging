---
title: Caching in ASP.NET Core
date: 2026-04-07
tags: aspnetcore, dotnet, csharp, tutorial
image: caching-in-aspnetcore.png
---

Caching is one of those things that looks simple on the surface — store a value, read it back later — but there's a surprising amount of nuance once you start applying it to real services. ASP.NET Core gives you several distinct caching mechanisms, and picking the right one for the job matters.

This post walks through the three main options: in-memory caching with `IMemoryCache`, distributed caching with `IDistributedCache`, and output caching for entire HTTP responses.

## IMemoryCache: The Quick Win

`IMemoryCache` stores data in the process's memory. It's the simplest option and requires no infrastructure — just register it and inject it. If you're running a single instance and your data isn't enormous, this is usually where you start.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

var app = builder.Build();
```

Inject and use it in a service:

```csharp
public class ProductService
{
    private readonly IMemoryCache _cache;
    private readonly IProductRepository _repository;

    public ProductService(IMemoryCache cache, IProductRepository repository)
    {
        _cache = cache;
        _repository = repository;
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        var key = $"product:{id}";

        if (_cache.TryGetValue(key, out Product? cached))
        {
            return cached;
        }

        var product = await _repository.GetByIdAsync(id);

        if (product is not null)
        {
            _cache.Set(key, product, TimeSpan.FromMinutes(5));
        }

        return product;
    }
}
```

That works, but there's a better helper method: `GetOrCreateAsync`. It handles the check-and-set in one go and avoids the race condition where two concurrent requests both miss the cache and both hit the database:

```csharp
public async Task<Product?> GetProductAsync(int id)
{
    return await _cache.GetOrCreateAsync($"product:{id}", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        entry.SlidingExpiration = TimeSpan.FromMinutes(2);
        return await _repository.GetByIdAsync(id);
    });
}
```

You can mix absolute and sliding expiration. Absolute expiration puts a hard ceiling on how long an entry can live; sliding expiration resets the clock each time the entry is accessed. The entry expires at whichever comes first — useful when you want to guarantee freshness but also evict stale entries that haven't been touched in a while.

### Eviction and Memory Pressure

`IMemoryCache` has a few knobs for controlling eviction priority. Entries marked `CacheItemPriority.NeverRemove` won't be evicted under memory pressure (use sparingly). The default is `Normal`, which allows eviction when the process is memory-constrained.

You can also register an eviction callback to react when an entry is removed:

```csharp
_cache.Set("config", config, new MemoryCacheEntryOptions()
    .SetAbsoluteExpiration(TimeSpan.FromHours(1))
    .RegisterPostEvictionCallback((key, value, reason, state) =>
    {
        // log or react to eviction
        Console.WriteLine($"Cache entry '{key}' evicted: {reason}");
    }));
```

This is handy for debugging cache behaviour — if entries are being evicted more aggressively than you expect, the eviction reason tells you why.

## IDistributedCache: Scaling Out

`IMemoryCache` falls apart the moment you run more than one instance. Each instance has its own cache, so clients hitting different instances get inconsistent results, and cache invalidation becomes a distributed systems problem.

`IDistributedCache` is the answer. It abstracts over a shared cache store — typically Redis or SQL Server — so all instances read from and write to the same place.

The interface is deliberately simple:

```csharp
Task<byte[]?> GetAsync(string key, CancellationToken token = default);
Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default);
Task RemoveAsync(string key, CancellationToken token = default);
```

You're working with `byte[]`, so you handle serialisation yourself. In practice, that usually means JSON:

```csharp
public class ProductService
{
    private readonly IDistributedCache _cache;
    private readonly IProductRepository _repository;

    public ProductService(IDistributedCache cache, IProductRepository repository)
    {
        _cache = cache;
        _repository = repository;
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        var key = $"product:{id}";
        var bytes = await _cache.GetAsync(key);

        if (bytes is not null)
        {
            return JsonSerializer.Deserialize<Product>(bytes);
        }

        var product = await _repository.GetByIdAsync(id);

        if (product is not null)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            };
            await _cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(product), options);
        }

        return product;
    }

    public async Task InvalidateProductAsync(int id)
    {
        await _cache.RemoveAsync($"product:{id}");
    }
}
```

### Redis Setup

For Redis, add the package and register the provider:

```bash
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
```

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "myapp:";
});
```

The `InstanceName` is a key prefix — it namespaces your keys so multiple applications can share the same Redis instance without colliding.

For local development, the in-memory implementation is a convenient stand-in:

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis");
        options.InstanceName = "myapp:";
    });
}
```

`IDistributedCache` is an interface, so your service code doesn't change — only the registration does.

## Output Caching: Caching at the HTTP Layer

Both `IMemoryCache` and `IDistributedCache` are object caches — you control what gets stored and when. Output caching is different: it caches entire HTTP responses at the middleware layer, before your code even runs.

This is the right tool when you have endpoints that return the same response for a given set of inputs, and you want to avoid executing the handler at all for cached requests. Think product catalogue pages, search results, or any read-heavy endpoint that doesn't vary per user.

Output caching was added to ASP.NET Core in .NET 7.

```csharp
builder.Services.AddOutputCache();

var app = builder.Build();

app.UseOutputCache();
```

Apply it to an endpoint:

```csharp
app.MapGet("/api/products", async (IProductRepository repo) =>
{
    var products = await repo.GetAllAsync();
    return Results.Ok(products);
})
.CacheOutput(p => p.Expire(TimeSpan.FromMinutes(10)));
```

The response is cached on first request and served from cache for subsequent ones — no database call, no handler execution.

### Varying the Cache by Request Data

The default cache key is based on the URL. If your endpoint varies by query string, route parameter, or header, you need to tell the middleware:

```csharp
app.MapGet("/api/products/{category}", async (string category, IProductRepository repo) =>
{
    var products = await repo.GetByCategoryAsync(category);
    return Results.Ok(products);
})
.CacheOutput(p => p
    .Expire(TimeSpan.FromMinutes(10))
    .VaryByRouteValue("category"));
```

```csharp
// Vary by query string parameter
app.MapGet("/api/search", async ([FromQuery] string q, ISearchService search) =>
    Results.Ok(await search.SearchAsync(q)))
.CacheOutput(p => p
    .Expire(TimeSpan.FromMinutes(5))
    .VaryByQuery("q"));
```

### Cache Invalidation with Tags

Output caching supports tag-based invalidation. Tag your cached responses, and you can purge all responses sharing a tag when the underlying data changes:

```csharp
app.MapGet("/api/products", async (IProductRepository repo) =>
    Results.Ok(await repo.GetAllAsync()))
.CacheOutput(p => p
    .Expire(TimeSpan.FromMinutes(10))
    .Tag("products"));

app.MapGet("/api/products/{id:int}", async (int id, IProductRepository repo) =>
    Results.Ok(await repo.GetByIdAsync(id)))
.CacheOutput(p => p
    .Expire(TimeSpan.FromMinutes(10))
    .Tag("products", $"product:{id}"));

// Invalidate from a mutation endpoint
app.MapPut("/api/products/{id:int}", async (
    int id,
    UpdateProductRequest request,
    IProductRepository repo,
    IOutputCacheStore cacheStore,
    CancellationToken ct) =>
{
    await repo.UpdateAsync(id, request);
    await cacheStore.EvictByTagAsync("products", ct);
    return Results.NoContent();
});
```

When a product is updated, all cached responses tagged with `"products"` are evicted. The next request rebuilds them from the database.

## Which One Should You Use?

They're not mutually exclusive — they solve different problems.

**`IMemoryCache`**: Single-instance deployments, session-scoped data, anything where you want zero serialisation overhead and sub-millisecond reads. Also a good staging area for data that changes frequently within a request lifecycle.

**`IDistributedCache`**: Multi-instance deployments (anything running in Kubernetes or behind a load balancer), shared application state, session data that needs to survive pod restarts. The serialisation cost is real but usually negligible compared to the database round-trip you're avoiding.

**Output caching**: Read-heavy API endpoints where the same request from different clients should return the same response. This is the most aggressive form of caching — the handler doesn't run at all — and it's the easiest to set up. The trade-off is less control: you're caching the entire response, not individual objects.

A realistic production setup often uses all three. Output cache the public-facing catalogue endpoints. Use `IDistributedCache` for session tokens and user-specific state that needs to be shared across instances. Use `IMemoryCache` as a fast local layer in front of `IDistributedCache` for frequently-read, rarely-changing reference data.

## The Cache Invalidation Problem

Cache invalidation is famously hard. A few practices that help:

**Prefer expiration over explicit invalidation** where possible. Time-based expiration is simple and predictable; explicit invalidation requires you to know every place data is cached when it changes.

**Keep TTLs short for mutable data.** A 30-second cache is often enough to absorb traffic spikes while keeping staleness tolerable.

**Use cache-aside, not write-through, for most cases.** Update the database, then invalidate (or let it expire). Write-through caching — updating cache and database simultaneously — sounds appealing but introduces consistency risks if either write fails.

**Never cache user-specific data without including the user identity in the cache key.** This one bites people regularly. An innocent-seeming output cache that doesn't vary by user can leak one user's data to another.

Getting the caching layer right saves significant infrastructure cost and makes services substantially more resilient under load. The built-in tooling in ASP.NET Core makes it straightforward to start simple and graduate to more sophisticated approaches as your needs evolve.
