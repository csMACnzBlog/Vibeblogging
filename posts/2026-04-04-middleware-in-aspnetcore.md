---
title: Middleware in ASP.NET Core
date: 2026-04-04
tags: aspnetcore, dotnet, csharp, tutorial
image: middleware-in-aspnetcore.png
---

Every HTTP request that hits an ASP.NET Core application passes through a pipeline before it ever reaches your endpoint. That pipeline is built from middleware — small pieces of code that each get a crack at the request (and the response on the way back out). Understand middleware and you understand how the whole framework hangs together.

I'll be honest: for a long time I treated middleware as something the framework configured for me. I'd call `app.UseAuthentication()` and `app.UseAuthorization()` in the right order and not think too hard about what was happening. Then I needed to write my own and suddenly that mental model collapsed pretty fast.

## What Middleware Actually Is

Each piece of middleware is a function that receives the current `HttpContext` and a delegate to the next piece of middleware in the chain. It can do work before calling next, after calling next, or both. It can also decide not to call next at all — short-circuiting the pipeline and returning a response directly.

```csharp
app.Use(async (context, next) =>
{
    // Before the rest of the pipeline
    Console.WriteLine($"Incoming: {context.Request.Method} {context.Request.Path}");

    await next(context);

    // After the rest of the pipeline
    Console.WriteLine($"Outgoing: {context.Response.StatusCode}");
});
```

That's it. Everything built into ASP.NET Core — authentication, routing, static files, exception handling — is implemented as middleware following exactly this pattern.

## The Order Matters. A Lot.

Middleware runs in the order you register it. The response flows back out in reverse order. Think of it like wrapping paper around a gift — each layer wraps the inner layers.

```csharp
var app = builder.Build();

app.UseExceptionHandler("/error");  // 1st in, last out
app.UseHttpsRedirection();          // 2nd in, 2nd to last out
app.UseStaticFiles();               // short-circuits for static content
app.UseRouting();
app.UseAuthentication();            // must come before UseAuthorization
app.UseAuthorization();
app.MapControllers();               // terminal middleware

app.Run();
```

If you put `UseAuthorization` before `UseAuthentication`, the auth check runs against an unauthenticated context — meaning nothing's been authenticated yet and everything gets blocked. I've made this mistake. It's not fun to debug.

The general rule: exception handling goes first (outermost), then HTTPS redirection, then static files, then routing, then auth, then your endpoints.

## app.Use vs app.Run vs app.Map

There are three ways to add middleware to the pipeline, and they mean different things.

`app.Use` adds middleware that can call the next delegate — the normal case:

```csharp
app.Use(async (context, next) =>
{
    // do something
    await next(context);
});
```

`app.Run` adds terminal middleware. It never calls next, so nothing after it runs:

```csharp
app.Run(async context =>
{
    await context.Response.WriteAsync("Short-circuited!");
});
```

`app.Map` branches the pipeline based on path. Everything you register inside the branch only runs for requests matching that path:

```csharp
app.Map("/health", branch =>
{
    branch.Run(async context =>
    {
        await context.Response.WriteAsync("Healthy");
    });
});
```

Requests to `/health` go down the branch. Everything else continues normally. You can nest these to get sophisticated routing before your endpoint routing even runs.

## Writing a Proper Middleware Class

Inline lambdas are fine for simple cases, but anything more complex deserves its own class. The convention is straightforward:

```csharp
public class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestTimingMiddleware(RequestDelegate _next)
    {
        this._next = _next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        await _next(context);

        stopwatch.Stop();
        var elapsed = stopwatch.ElapsedMilliseconds;

        context.Response.Headers["X-Elapsed-Ms"] = elapsed.ToString();
    }
}
```

Register it with an extension method — this is the pattern you'll see in every well-structured middleware library:

```csharp
public static class RequestTimingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestTiming(
        this IApplicationBuilder app)
        => app.UseMiddleware<RequestTimingMiddleware>();
}
```

Then in `Program.cs`:

```csharp
app.UseRequestTiming();
```

The `UseMiddleware<T>()` call handles instantiation and wires up the `RequestDelegate` parameter automatically. If your middleware has other constructor dependencies, they get resolved from DI too.

## Per-Request Dependencies

Here's something that trips people up. Middleware classes are instantiated once — they're effectively singletons. But your middleware might need scoped services like a database context.

Don't inject scoped services into the constructor. Instead, receive them as parameters on `InvokeAsync`:

```csharp
public class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAuditLogger auditLogger)
    {
        await _next(context);

        await auditLogger.LogAsync(
            context.Request.Path,
            context.Response.StatusCode,
            context.User.Identity?.Name);
    }
}
```

ASP.NET Core resolves the parameters on `InvokeAsync` from the current request's scope, so `IAuditLogger` can be scoped or transient and everything works correctly. If you inject it via the constructor instead, you'll get a singleton version of a scoped service — which usually means weird state bugs that only show up under load.

## Short-Circuiting the Pipeline

Sometimes you want middleware to stop everything and return immediately. Rate limiters, IP allowlists, and health check endpoints all do this:

```csharp
public class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowedIps;

    public IpAllowlistMiddleware(RequestDelegate next, IOptions<IpAllowlistOptions> options)
    {
        _next = next;
        _allowedIps = [..options.Value.AllowedIps];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();

        if (remoteIp is null || !_allowedIps.Contains(remoteIp))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return; // don't call _next — pipeline stops here
        }

        await _next(context);
    }
}
```

No call to `_next`, no further middleware runs. The response goes back immediately. It's one of those patterns that feels obvious once you see it, but takes a moment to click if you're used to frameworks where the pipeline is more opaque.

## Exception Handling Middleware

Exception handling middleware is worth calling out separately because it's a great example of the "wrap both sides" pattern:

```csharp
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "An unexpected error occurred."
            });
        }
    }
}
```

This catches exceptions from anything downstream in the pipeline — your routing, your endpoints, your services — because it wraps them all. That's why exception handling middleware goes first in the registration order.

ASP.NET Core ships `UseExceptionHandler` and `UseDeveloperExceptionPage` that do this for you, but understanding the pattern means you can customise it when you need to.

## Middleware vs Filters

One thing that confused me early on: what's the difference between middleware and filters (action filters, resource filters, etc.)?

The short answer: middleware operates on the raw HTTP pipeline. Filters are closer to the MVC layer and have access to controller context, action arguments, and the like.

As a rule of thumb:
- Use middleware for cross-cutting concerns that don't need MVC context: logging, caching, security headers, rate limiting.
- Use filters when you need access to controller metadata or action results: validation, response shaping, audit logging that needs the action name.

They're not competing — they're complementary layers of the same pipeline.

## Middleware Is Everywhere

Once you see it, you can't unsee it. `app.UseStaticFiles()` — middleware that checks if the request maps to a file on disk and short-circuits if so. `app.UseRouting()` — middleware that matches the request path to an endpoint and stashes that match in `HttpContext`. `app.UseAuthentication()` — middleware that reads credentials from the request and populates `HttpContext.User`.

Every call to `app.Use*` is adding another layer to the same pipeline. Understanding that changes how you read framework documentation and how quickly you can diagnose why something isn't working.

The pipeline model is one of ASP.NET Core's best design decisions. It's explicit, composable, and once it clicks it makes the whole framework feel much less magical.
