---
title: HybridCache in ASP.NET Core
date: 2026-05-26
tags: aspnetcore, dotnet, csharp, caching, performance
image: hybridcache-in-aspnetcore.png
---

# HybridCache in ASP.NET Core

I like caching right up until I have to explain which cache API we should use this time.

`IMemoryCache` is nice and fast. `IDistributedCache` is nice and shared. Then you end up writing the same “check cache, fetch data, serialize data, store data, hope 30 concurrent requests don’t all stampede the database at once” code again. It works, but it’s hardly the most glamorous part of the job.

That’s where `HybridCache` is interesting. It gives you a single API that can sit over in-memory caching and, if you configure it, a distributed backend as well. Better yet, it handles cache population in one place so you don’t need to hand-roll the usual miss logic every time.

## Why I’d reach for it

The real selling point is that `HybridCache` smooths over a few annoyances that show up in real apps:

- one API instead of separate “memory cache here, distributed cache there” code paths
- built-in cache population with `GetOrCreateAsync`
- local in-process caching for speed
- optional distributed backing store for multi-instance apps
- less duplicate boilerplate around serialization and expiry decisions

It’s not magic. You still need to pick sensible keys, expirations, and invalidation points. But it removes a bunch of plumbing that I’d rather not keep rewriting.

## 1) Register HybridCache

Start with the package:

```bash
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

Then register it in your ASP.NET Core app:

```csharp
using Microsoft.Extensions.Caching.Hybrid;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});
```

That gives you a fast local cache with a default ten-minute overall lifetime. The shorter `LocalCacheExpiration` lets each app instance keep a near copy without hanging on to it forever.

If you stop here, you still get a useful API and in-memory caching. That’s already decent.

## 2) Use `GetOrCreateAsync` in an endpoint or service

Here’s a minimal API example for product lookups:

```csharp
using Microsoft.Extensions.Caching.Hybrid;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHybridCache();
builder.Services.AddSingleton<IProductRepository, ProductRepository>();

var app = builder.Build();

app.MapGet("/products/{id:int}", async (
    int id,
    HybridCache cache,
    IProductRepository repository,
    CancellationToken ct) =>
{
    var product = await cache.GetOrCreateAsync(
        $"product:{id}",
        async cancel => await repository.GetByIdAsync(id, cancel),
        options: new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(10),
            LocalCacheExpiration = TimeSpan.FromMinutes(1)
        },
        cancellationToken: ct);

    return product is null ? Results.NotFound() : Results.Ok(product);
});

app.Run();

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task UpdatePriceAsync(int id, decimal price, CancellationToken cancellationToken);
}

public sealed class ProductRepository : IProductRepository
{
    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken)
        => Task.FromResult<Product?>(new Product(id, $"Product {id}", 19.95m));

    public Task UpdatePriceAsync(int id, decimal price, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed record Product(int Id, string Name, decimal Price);
```

The important bit is that the fetch callback only runs when the cache misses. That means your normal code becomes “tell the cache how to get the data” instead of “write the full caching ceremony again”.

Simple.

And that simplicity matters because caching code has a habit of quietly becoming messy code.

## 3) Add Redis when you need shared caching

If your app runs on more than one instance, the local cache alone won’t be enough. This is where the “hybrid” part gets more interesting.

Register a distributed cache provider such as Redis alongside `HybridCache`:

```bash
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
```

```csharp
using Microsoft.Extensions.Caching.Hybrid;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("cache");
    options.InstanceName = "products:";
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});
```

Now each instance can keep a very short-lived local copy while still sharing cached data through Redis. That’s a nice balance: fast reads in-process, with consistency across instances that’s much better than plain `IMemoryCache`.

## 4) Don’t forget invalidation

The nice API doesn’t save you from stale data. If a product changes, you still need to remove or refresh the cached value.

```csharp
app.MapPost("/products/{id:int}/price", async (
    int id,
    decimal price,
    HybridCache cache,
    IProductRepository repository,
    CancellationToken ct) =>
{
    await repository.UpdatePriceAsync(id, price, ct);
    await cache.RemoveAsync($"product:{id}", ct);
    return Results.NoContent();
});
```

That’s the bit people conveniently forget when a demo is going well.

A cache that never gets invalidated is basically a very enthusiastic liar.

## A few practical rules

A few things I’d keep in mind if I were adding `HybridCache` to a real service:

- keep keys predictable and boring (`product:123` beats “some clever custom format”)
- use short local expirations when you have multiple instances
- don’t cache everything just because you can
- invalidate on writes, not “whenever we remember”
- measure whether the cache is actually helping before congratulating yourself too much

I also wouldn’t use `HybridCache` as an excuse to stop thinking about data shape. If you’re caching giant objects, or highly user-specific responses, or data that changes every few seconds, the problem might not be “which cache API?” at all.

## Final thought

I think `HybridCache` hits a nice middle ground.

It keeps the call site clean, works well for the common “read-heavy lookup with occasional writes” shape, and gives you a path from simple local caching to something more distributed without rewriting your whole service layer. That’s a pretty good trade.

If you’ve been bouncing between `IMemoryCache` and `IDistributedCache` and feeling mildly annoyed about it, `HybridCache` is worth a look.
