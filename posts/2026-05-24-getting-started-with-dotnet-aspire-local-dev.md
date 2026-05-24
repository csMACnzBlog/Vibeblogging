---
title: Getting Started with .NET Aspire Local Dev
date: 2026-05-24
tags: dotnet, aspire, csharp, architecture
image: getting-started-with-dotnet-aspire-local-dev.png
---

# Getting Started with .NET Aspire Local Dev

If you’ve ever stitched together 2–5 local services, a database, and a queue just to test one feature, you already know the pain. .NET Aspire makes that setup much easier.

You get one app host to orchestrate everything, built-in service discovery, health checks, and a dashboard that actually helps when things go sideways.

Let’s build a small distributed app and run it locally.

## What we’re building

A simple setup with:

- `catalogapi` (minimal API)
- `webfrontend` (minimal API calling `catalogapi`)
- `cache` (Redis container)

Aspire will start and wire all of it from one place.

## 1) Create the starter solution

```bash
dotnet workload install aspire
dotnet new aspire-starter -n AspireGettingStarted
cd AspireGettingStarted
```

This gives you an `AppHost` project plus service projects.

## 2) Orchestrate services in AppHost

Open `AppHost/Program.cs` and wire resources/projects:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var catalogApi = builder.AddProject<Projects.CatalogApi>("catalogapi")
    .WithReference(cache);

builder.AddProject<Projects.WebFrontend>("webfrontend")
    .WithReference(catalogApi);

builder.Build().Run();
```

That’s the “local distributed environment” in one file.

## 3) Add a practical API endpoint with Redis caching

In `CatalogApi`, add Redis caching support:

```bash
dotnet add CatalogApi package Microsoft.Extensions.Caching.StackExchangeRedis
```

Then use it in `CatalogApi/Program.cs`:

```csharp
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("cache");
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/products/{id:int}", async (int id, IDistributedCache cache, CancellationToken ct) =>
{
    var key = $"product:{id}";
    var fromCache = await cache.GetStringAsync(key, ct);

    if (fromCache is not null)
    {
        return Results.Ok(new { id, name = fromCache, source = "cache" });
    }

    var name = $"Product {id}";
    await cache.SetStringAsync(
        key,
        name,
        new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        },
        ct);

    return Results.Ok(new { id, name, source = "api" });
});

app.Run();
```

## 4) Call the API from another service

In `WebFrontend/Program.cs`:

```csharp
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("catalog", client =>
{
    client.BaseAddress = new Uri("http://catalogapi");
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", async (IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("catalog");
    var product = await client.GetFromJsonAsync<ProductDto>("/products/1");
    return Results.Ok(product);
});

app.Run();

public record ProductDto(int Id, string Name, string Source);
```

No hardcoded localhost ports between services. Aspire handles discovery.

## 5) Run everything

```bash
dotnet run --project AppHost
```

You should see Aspire launch your services and the dashboard. Hit `webfrontend`, then refresh a couple of times—you’ll see `source` flip from `"api"` to `"cache"`.

## Why this is a great starting point

For local distributed development, Aspire gives you a clean default:

- One command to run the whole app graph
- Consistent wiring between services/resources
- Better observability while developing
- Less “works on my machine” config drift

If you’re starting microservices (or even just “a couple of services”), this is the nicest on-ramp I’ve used in .NET.
