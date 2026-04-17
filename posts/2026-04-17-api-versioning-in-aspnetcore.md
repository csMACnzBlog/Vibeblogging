---
title: API Versioning in ASP.NET Core
date: 2026-04-17
tags: aspnetcore, dotnet, csharp, api, tutorial
image: api-versioning-in-aspnetcore.png
---

APIs grow. Endpoints change shape, fields get renamed, response contracts evolve — and at some point you need to make a breaking change without destroying every existing client that depends on the old behaviour.

That's what API versioning is for. Let's walk through how to set it up cleanly in ASP.NET Core.

## Install the Package

Microsoft ships an official versioning package for ASP.NET Core. Add it:

```bash
dotnet add package Asp.Versioning.Http
```

If you're using controllers instead of minimal APIs, there's a controller-specific package:

```bash
dotnet add package Asp.Versioning.Mvc
```

## Register Versioning in Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

var app = builder.Build();
```

`AssumeDefaultVersionWhenUnspecified` means clients that don't pass any version get v1. `ReportApiVersions` adds `api-supported-versions` and `api-deprecated-versions` headers to every response — handy for clients to discover what's available.

## URL Segment Versioning

The most common approach puts the version right in the URL. It's explicit and easy to read in logs.

```csharp
var versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0))
    .Build();

app.MapGet("/api/v{version:apiVersion}/products", () =>
{
    return Results.Ok(new[] { new { Id = 1, Name = "Widget" } });
})
.WithApiVersionSet(versionSet)
.MapToApiVersion(new ApiVersion(1, 0));

app.MapGet("/api/v{version:apiVersion}/products", () =>
{
    // v2 returns richer data
    return Results.Ok(new[] { new { Id = 1, Name = "Widget", Sku = "WGT-001" } });
})
.WithApiVersionSet(versionSet)
.MapToApiVersion(new ApiVersion(2, 0));
```

Clients call `/api/v1/products` or `/api/v2/products` — clean and obvious.

## Query String Versioning

If you'd rather not change URL structure, query string versioning is the fallback:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = new QueryStringApiVersionReader("api-version");
});
```

Clients now pass the version as a parameter: `/products?api-version=2.0`.

## Header Versioning

Some teams prefer keeping URLs clean and putting the version in a custom header:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = new HeaderApiVersionReader("X-Api-Version");
});
```

Clients include `X-Api-Version: 2.0` in the request. Less visible than URL versioning, but keeps your routes tidy.

## Combining Multiple Readers

You can support all three strategies simultaneously:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("X-Api-Version")
    );
});
```

The readers are checked in order. If the URL contains a version segment, it wins. Otherwise the query string is checked, then the header.

## Deprecating Old Versions

When you're ready to retire a version, mark it deprecated rather than removing it immediately:

```csharp
var versionSet = app.NewApiVersionSet()
    .HasDeprecatedApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0))
    .Build();
```

Clients calling v1 will still get a valid response, but the `api-deprecated-versions: 1.0` header in the response tells them they're on borrowed time. This gives teams a proper migration window instead of a sudden break.

## Versioning with Controllers

If you're using controllers, the pattern is similar but uses attributes:

```csharp
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public IActionResult GetProducts()
    {
        return Ok(new[] { new { Id = 1, Name = "Widget" } });
    }
}

[ApiController]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductsV2Controller : ControllerBase
{
    [HttpGet]
    public IActionResult GetProducts()
    {
        return Ok(new[] { new { Id = 1, Name = "Widget", Sku = "WGT-001" } });
    }
}
```

You can also map multiple versions to the same controller using `[MapToApiVersion]`:

```csharp
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet]
    [MapToApiVersion("1.0")]
    public IActionResult GetV1() => Ok("v1 orders");

    [HttpGet]
    [MapToApiVersion("2.0")]
    public IActionResult GetV2() => Ok("v2 orders with pagination");
}
```

## A Few Practical Tips

**Version at the API boundary, not inside business logic.** Your services and domain models shouldn't know about API versions — that's the controller/endpoint's job. Map different request/response DTOs at the edge.

**Don't create a new version for every change.** Adding a new optional field or a new endpoint is non-breaking. You only need a new version when you're removing something, renaming something, or changing the shape of an existing response.

**Set a deprecation timeline and stick to it.** A version marked deprecated with no removal date just accumulates forever. Pick a date, communicate it, and honour it.

## Wrapping Up

API versioning in ASP.NET Core is straightforward once you have the package in place. Pick a versioning strategy that fits how your clients work (URL segments are the most obvious, headers are the cleanest), register it in `Program.cs`, and you're done.

The key is to plan for versioning before you need it rather than retrofitting it after the first breaking change forces your hand.
