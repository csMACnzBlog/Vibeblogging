---
title: Rate Limiting in ASP.NET Core
date: 2026-04-05
tags: aspnetcore, dotnet, csharp, tutorial
image: rate-limiting-in-aspnetcore.png
---

Every public API is one viral moment away from being hammered into the ground. Rate limiting is the mechanism that stands between your service and the flood — it caps how many requests a client can make in a given window and politely (or firmly) tells them to slow down when they exceed it.

ASP.NET Core has had third-party rate limiting options for years, but .NET 7 brought first-class built-in support via the `Microsoft.AspNetCore.RateLimiting` middleware. No extra packages required, no fighting with someone else's abstractions. It's baked right in.

## The Four Policies You'll Actually Use

Before touching any code, it helps to understand what each limiter actually does. The framework ships with four algorithms, and they have meaningfully different behaviour.

**Fixed Window** — counts requests in a fixed time slot (e.g. 100 requests per minute). Simple and cheap. The downside: a client can hit you with 100 requests in the last second of one window and 100 more in the first second of the next, effectively doubling their burst.

**Sliding Window** — fixes the burst problem by dividing the window into segments and rolling the count forward. More accurate, slightly more memory.

**Token Bucket** — clients accumulate tokens over time and spend one per request. They can burst up to the bucket size, then they're rate-limited until tokens refill. Great when you want to allow short bursts but control average throughput.

**Concurrency** — limits how many requests are *in flight at once*, not how many happen per unit of time. Useful for protecting downstream resources that can only handle N simultaneous calls.

I'll be honest: for most APIs, fixed window is fine and everyone reaches for it first. Sliding window is worth the upgrade if you're seeing abuse at window boundaries. Token bucket is the right tool for bursty-but-fair clients.

## Wiring It Up

Setup lives in `Program.cs`. You register the limiter policies, then add the middleware.

```csharp
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
});

var app = builder.Build();

app.UseRateLimiter();

app.MapGet("/", () => "Hello!").RequireRateLimiting("fixed");

app.Run();
```

`QueueLimit = 0` means excess requests are rejected immediately. Set it higher if you want the middleware to hold requests and retry once a slot opens — useful for background jobs, less useful for interactive APIs where you want a fast failure.

## Adding the Other Policies

Here's how you'd configure all four side by side so you can see the pattern:

```csharp
builder.Services.AddRateLimiter(options =>
{
    // Fixed window: 100 requests per minute
    options.AddFixedWindowLimiter("fixed", o =>
    {
        o.PermitLimit = 100;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });

    // Sliding window: 100 requests per minute, 4 segments
    options.AddSlidingWindowLimiter("sliding", o =>
    {
        o.PermitLimit = 100;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 4;
        o.QueueLimit = 0;
    });

    // Token bucket: refill 10 tokens every 5 seconds, burst up to 50
    options.AddTokenBucketLimiter("token", o =>
    {
        o.TokenLimit = 50;
        o.ReplenishmentPeriod = TimeSpan.FromSeconds(5);
        o.TokensPerPeriod = 10;
        o.QueueLimit = 0;
    });

    // Concurrency: max 20 requests in flight at once
    options.AddConcurrencyLimiter("concurrency", o =>
    {
        o.PermitLimit = 20;
        o.QueueLimit = 5;
    });
});
```

The naming convention you pick for the policy strings is your own — they're just keys you reference when applying the limiter to endpoints.

## Applying Policies to Endpoints

You've got two main options: apply a policy to individual endpoints, or set a global default.

Per-endpoint — useful when different routes have different tolerance levels:

```csharp
app.MapGet("/api/search", SearchHandler)
    .RequireRateLimiting("sliding");

app.MapPost("/api/upload", UploadHandler)
    .RequireRateLimiting("concurrency");

// This route is exempt from any global policy
app.MapGet("/health", HealthHandler)
    .DisableRateLimiting();
```

Global default — applies to everything that doesn't opt out:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User?.Identity?.Name ?? context.Request.Headers.Host.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

The `PartitionedRateLimiter` approach is more powerful — it lets you partition the limit by user, IP, API key, or any other dimension you can extract from the request context. You'll want this in production rather than a single shared counter for all clients.

## Handling Rejected Requests

By default, the middleware returns a 503 Service Unavailable when the limit is hit. That's wrong for rate limiting — the correct status is **429 Too Many Requests**. Fix it in the options:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            await context.HttpContext.Response.WriteAsync(
                $"Too many requests. Please retry after {retryAfter.TotalSeconds} seconds.",
                cancellationToken);
        }
        else
        {
            await context.HttpContext.Response.WriteAsync(
                "Too many requests. Please try again later.",
                cancellationToken);
        }
    };
});
```

The `RetryAfter` metadata tells the client when they can try again — include it whenever you can. A well-behaved client will respect it; a well-designed API always provides it.

## A Real-World Example: Protecting a Public API

Here's a pattern I've settled on for public APIs: a lenient global limit for authenticated users, a stricter limit for anonymous traffic, and no limit on internal health/status endpoints.

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        // Outer: per-user or per-IP limit
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var userId = context.User?.Identity?.Name;

            if (userId is not null)
            {
                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 500,
                        Window = TimeSpan.FromMinutes(1)
                    });
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1)
                });
        }),

        // Inner: global concurrency cap regardless of user
        PartitionedRateLimiter.Create<HttpContext, string>(_ =>
            RateLimitPartition.GetConcurrencyLimiter("global", _ =>
                new ConcurrencyLimiterOptions { PermitLimit = 100 }))
    );
});
```

`CreateChained` applies multiple limiters in sequence — a request has to pass all of them. Authenticated users get 500 requests per minute; anonymous traffic gets 60. Everyone is subject to the concurrency cap.

## Worth the Fifteen Minutes

Rate limiting is one of those things that feels optional until the moment it isn't. Adding it to a new API takes fifteen minutes and the built-in middleware is genuinely good — well-designed API, sensible defaults, and enough flexibility to handle most real-world scenarios without reaching for a third-party library.

The bit I keep coming back to is how composable it all is. You can mix policies, chain limiters, partition by any request attribute, and hook into rejection handling — all through a clean fluent API. It's a good example of the kind of infrastructure that's much less painful to add at the start of a project than to bolt on later when things are already on fire.
