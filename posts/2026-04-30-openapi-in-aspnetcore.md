---
title: Built-in OpenAPI in ASP.NET Core
date: 2026-04-30
tags: dotnet, aspnetcore, api, tutorial
image: openapi-in-aspnetcore.png
---

For years, if you wanted OpenAPI support in an ASP.NET Core app, you reached for Swashbuckle. It's been the de-facto standard — add the NuGet package, sprinkle some XML doc comments, call `AddSwaggerGen()`, and you'd get a `/swagger` UI and a generated spec. It worked, but it always felt like a third-party bolt-on rather than something the framework actually understood.

.NET 9 changed that by introducing first-party OpenAPI support via `Microsoft.AspNetCore.OpenApi`. .NET 10 polished it further. The framework now knows about your API's shape and can generate an OpenAPI specification without any external dependencies.

## Adding the Package

Start with a new or existing ASP.NET Core project and add the package:

```bash
dotnet add package Microsoft.AspNetCore.OpenApi
```

That's the only dependency you need. No Swashbuckle, no NSwag, just the official Microsoft package.

## Wiring It Up

In `Program.cs`, register the OpenAPI services and map the endpoint:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "Hello, World!");
app.Run();
```

`MapOpenApi()` exposes the generated specification at `/openapi/v1.json`. Hit that endpoint and you'll get a complete OpenAPI 3.0 document describing your API. No configuration file, no attributes required — just the convention-based spec derived from your endpoints.

## Serving a Swagger UI

The built-in support generates the JSON spec, but it doesn't ship with a UI renderer. For development, you'll want something visual. The easiest option is Scalar, which provides a modern API reference UI:

```bash
dotnet add package Scalar.AspNetCore
```

```csharp
using Scalar.AspNetCore;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
```

`MapScalarApiReference()` serves a UI at `/scalar/v1` that reads from the JSON spec automatically. It's clean, fast, and actively maintained.

If you'd rather stick with the classic Swagger UI, you can still use it — just point it at the `/openapi/v1.json` endpoint instead of the Swashbuckle-generated one. The spec format is the same.

## Describing Your Endpoints

The framework infers a lot just from your method signatures — parameter types, return types, route patterns. But you'll want to add descriptions for a useful API reference. All the relevant extension methods live on `RouteHandlerBuilder`:

```csharp
app.MapGet("/products/{id}", (int id) => Results.Ok(new Product(id, "Widget", 9.99m)))
    .WithName("GetProduct")
    .WithSummary("Get a product by ID")
    .WithDescription("Returns the product details for the given ID")
    .Produces<Product>()
    .Produces(404)
    .WithTags("Products");
```

`WithSummary()` is the short one-liner that appears in the endpoint list. `WithDescription()` is the longer explanation shown when you expand the endpoint. `WithTags()` groups endpoints together in the UI — handy once you've got more than a handful of routes.

`Produces<T>()` tells the spec what type comes back on a successful response. `Produces(404)` documents the not-found case. These are straightforward and replace the verbose `[ProducesResponseType]` attributes from controller-based APIs.

## A Complete Example

Here's a minimal products API with full OpenAPI documentation wired up:

```csharp
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

var products = new List<Product>
{
    new(1, "Widget", 9.99m),
    new(2, "Gadget", 24.99m),
};

app.MapGet("/products", () => Results.Ok(products))
    .WithSummary("List all products")
    .Produces<List<Product>>()
    .WithTags("Products");

app.MapGet("/products/{id}", (int id) =>
{
    var product = products.FirstOrDefault(p => p.Id == id);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
    .WithName("GetProduct")
    .WithSummary("Get a product by ID")
    .Produces<Product>()
    .Produces(404)
    .WithTags("Products");

app.MapPost("/products", (Product product) =>
{
    products.Add(product);
    return Results.Created($"/products/{product.Id}", product);
})
    .WithSummary("Create a product")
    .Produces<Product>(201)
    .WithTags("Products");

app.Run();

record Product(int Id, string Name, decimal Price);
```

Start this up and navigate to `/scalar/v1`. You'll see a fully documented API reference without writing a single XML comment.

## Customising the Document

By default the spec has a generic title and version. You can customise document-level metadata via the options callback:

```csharp
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new()
        {
            Title = "Products API",
            Version = "v1",
            Description = "A simple example products API built with ASP.NET Core.",
            Contact = new() { Name = "Your Team", Email = "api@example.com" }
        };
        return Task.CompletedTask;
    });
});
```

Document transformers let you modify the generated `OpenApiDocument` object directly. You can add security schemes, set servers, tweak any part of the spec programmatically. It's more verbose than a YAML config file, but it's strongly-typed and plays nicely with dependency injection — you can inject configuration or other services into your transformers.

## What's Different from Swashbuckle

The most obvious difference is that this is built into the framework. The OpenAPI generation understands minimal API conventions natively, so it infers things correctly without workarounds. Swashbuckle was designed around controllers first and had to play catch-up with minimal APIs.

There's no XML doc dependency. Swashbuckle traditionally relied on XML documentation comments and a project setting to include the XML file in the output. This package works entirely through the fluent API — `WithSummary()` and `WithDescription()` replace the XML comments.

It's also AOT-compatible. If you're heading down the Native AOT path (and .NET 10 makes that increasingly practical), Swashbuckle will cause problems because it relies on reflection. The built-in package is designed to work without runtime reflection.

The trade-off is maturity. Swashbuckle has years of community extensions, UI themes, and edge-case handling. The Microsoft package is newer and doesn't yet cover every scenario Swashbuckle handles. For most projects it's more than enough, but if you're doing something exotic, check that your specific requirements are supported.

## Wrapping Up

If you're starting a new ASP.NET Core project on .NET 9 or 10, there's a strong case for skipping Swashbuckle entirely and using the built-in OpenAPI support from the start. It's lighter, better integrated with the framework, and AOT-friendly.

For existing projects, migrating isn't urgent — Swashbuckle still works fine. But if you're upgrading to .NET 9 or 10 anyway and want to reduce third-party dependencies, the migration is straightforward: swap the package references, replace `AddSwaggerGen()` with `AddOpenApi()`, and update the endpoint metadata calls.

The direction of travel is clear. Microsoft is investing here, and the built-in support will only get more complete over time.
