---
title: AsyncLocal in C#: Context That Flows
date: 2026-05-31
tags: dotnet, csharp, async, aspnetcore
image: asynclocal-in-csharp-context-that-flows.png
---

`AsyncLocal<T>` is one of those features I ignored for way too long.

When I finally needed correlation IDs to show up everywhere (controllers, services, logs, background work), passing one more parameter through ten methods got old fast. `AsyncLocal<T>` gave me ambient context that flows with async calls, and it cleaned up a lot of noisy plumbing.

## The core idea

`AsyncLocal<T>` stores data in the current async control flow. That means each request can have its own value even when many requests run concurrently.

```csharp
using System;
using System.Threading;

public static class RequestContext
{
    private static readonly AsyncLocal<string?> CorrelationId = new();

    public static string? CurrentCorrelationId
    {
        get => CorrelationId.Value;
        set => CorrelationId.Value = value;
    }
}
```

I like this because callers don't need to thread a `correlationId` argument through every method.

## Set it once in middleware

In ASP.NET Core, middleware is a good place to initialize context.

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        RequestContext.CurrentCorrelationId =
            context.Request.Headers.TryGetValue("X-Correlation-ID", out var value)
                ? value.ToString()
                : Guid.NewGuid().ToString("N");

        try
        {
            await next(context);
        }
        finally
        {
            RequestContext.CurrentCorrelationId = null;
        }
    }
}
```

The `finally` matters. Clearing the value prevents accidental leaks when code is reused across logical operations.

## Use it in services without extra parameters

Once set, any downstream async method can read it.

```csharp
using Microsoft.Extensions.Logging;

public sealed class BillingService(ILogger<BillingService> logger)
{
    public Task ChargeAsync(decimal amount, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Charging {Amount} with CorrelationId {CorrelationId}",
            amount,
            RequestContext.CurrentCorrelationId ?? "missing");

        return Task.CompletedTask;
    }
}
```

That's usually enough for request-scoped logging and diagnostics.

## One gotcha: background work inherits context by default

If you queue fire-and-forget work with `Task.Run`, execution context (including `AsyncLocal`) can flow into that task.

Sometimes that's useful. Sometimes it's confusing.

When I *don't* want flow, I suppress it explicitly:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

public static class BackgroundDispatcher
{
    public static Task RunDetached(Func<Task> work)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(work);
        }
    }
}
```

That gives the background task a clean context boundary.

## Quick rule of thumb

I use `AsyncLocal<T>` for cross-cutting metadata like:

- Correlation IDs
- Tenant IDs
- Trace context

I avoid it for core business data that should be explicit in method signatures.

## Final thought

`AsyncLocal<T>` is great when you're trying to reduce parameter noise without losing context. Just be intentional about where values are set, always clear them, and decide when context should (or shouldn't) flow into background work.
