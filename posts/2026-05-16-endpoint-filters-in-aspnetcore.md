---
title: Endpoint Filters in ASP.NET Core
date: 2026-05-16
tags: aspnetcore, dotnet, csharp, api, middleware
image: endpoint-filters-in-aspnetcore.png
---

Minimal APIs are great right up until each endpoint starts repeating the same guard code. Validate input, check headers, enforce tenant context, shape errors — and suddenly every handler has the same boilerplate.

Endpoint filters are a clean way to pull that cross-cutting logic out of handlers without dropping back to full middleware for everything.

## Why Filters Exist

Middleware runs for every request. That's perfect for global concerns like logging, auth, or correlation IDs.

But sometimes the rule is endpoint-specific: this route needs a custom header, that route needs payload validation, another one needs to short-circuit based on business preconditions.

That's the sweet spot for endpoint filters.

## A Simple Validation Filter

```csharp
using Microsoft.AspNetCore.Http.HttpResults;

public sealed class ValidateOrderFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.GetArgument<CreateOrderRequest>(0);

        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            return TypedResults.BadRequest(new { error = "customerId is required" });
        }

        if (request.Lines.Count == 0)
        {
            return TypedResults.BadRequest(new { error = "At least one line item is required" });
        }

        return await next(context);
    }
}
```

The filter gets called before the endpoint handler, and can either short-circuit with a response or continue with `next(context)`.

## Wiring It to an Endpoint

```csharp
app.MapPost("/orders", async (CreateOrderRequest request, IOrderService service) =>
{
    var orderId = await service.CreateAsync(request);
    return Results.Created($"/orders/{orderId}", new { orderId });
})
.AddEndpointFilter<ValidateOrderFilter>();
```

Now the endpoint stays focused on business flow, and validation remains reusable.

## Passing Shared Context Between Filters

You can stash values in `HttpContext.Items` when a filter computes something expensive:

```csharp
public sealed class TenantResolutionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var tenantId = context.HttpContext.Request.Headers["X-Tenant-Id"].ToString();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.BadRequest(new { error = "Missing X-Tenant-Id header" });
        }

        context.HttpContext.Items["tenant-id"] = tenantId;
        return await next(context);
    }
}
```

Your handler can then read the resolved value instead of re-parsing headers.

## Group-Level Filters

You can apply filters to a route group so every endpoint in that group gets the same rule:

```csharp
var admin = app.MapGroup("/admin")
    .AddEndpointFilter<RequireAdminHeaderFilter>();

admin.MapGet("/metrics", () => Results.Ok());
admin.MapPost("/reindex", () => Results.Accepted());
```

This keeps setup tidy and avoids copy-paste registrations.

## When to Use Middleware Instead

Use middleware when a concern is truly app-wide. Use endpoint filters when behavior belongs to specific routes or groups.

That split keeps both layers simple: middleware for global policy, filters for endpoint policy.

## Wrapping Up

Endpoint filters are one of those features that quietly improve code quality. Handlers stay small, repeated guards disappear, and endpoint-specific rules become explicit and testable.

If your Minimal API handlers are getting noisy, adding a couple of focused filters is usually the fastest cleanup you can make.
