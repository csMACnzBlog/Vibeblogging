---
title: Using ProblemDetails in ASP.NET Core APIs
date: 2026-05-17
tags: aspnetcore, dotnet, csharp, api, errors
image: using-problemdetails-in-aspnetcore-apis.png
---

Inconsistent error payloads make APIs harder to consume than they need to be. One endpoint returns `{ message: "..." }`, another returns `{ error: "..." }`, and validation failures have a completely different shape again.

`ProblemDetails` gives you one standard structure for all of it.

## Why It Helps

`ProblemDetails` follows RFC 7807 and gives clients a predictable schema:

- `type`
- `title`
- `status`
- `detail`
- `instance`

That consistency means less custom parsing and fewer fragile client-side workarounds.

## Enable ProblemDetails Globally

```csharp
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
```

With that in place, unhandled exceptions and plain status code responses can be surfaced as proper problem payloads.

## Return Typed Problems from Handlers

```csharp
app.MapGet("/products/{id:int}", async (int id, IProductRepository repo) =>
{
    var product = await repo.FindAsync(id);

    if (product is null)
    {
        return Results.Problem(
            title: "Product not found",
            detail: $"No product exists with id {id}.",
            statusCode: StatusCodes.Status404NotFound,
            type: "https://httpstatuses.com/404");
    }

    return Results.Ok(product);
});
```

Now your 404 is machine-readable and consistent with other API errors.

## Add Domain-Specific Extensions

You can include extra fields when they're useful for clients:

```csharp
return Results.Problem(
    title: "Insufficient stock",
    detail: "Requested quantity exceeds available stock.",
    statusCode: StatusCodes.Status409Conflict,
    extensions: new Dictionary<string, object?>
    {
        ["sku"] = request.Sku,
        ["available"] = inventory.Available,
        ["requested"] = request.Quantity
    });
```

These extension fields are ideal for structured client handling and better UI messages.

## Validation Errors

For model validation scenarios, return `ValidationProblem`:

```csharp
if (!validator.TryValidate(request, out var failures))
{
    var errors = failures
        .GroupBy(f => f.PropertyName)
        .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray());

    return Results.ValidationProblem(errors);
}
```

This keeps field-level errors in a standard format most frontend frameworks already expect.

## Wrapping Up

`ProblemDetails` is a small change with a big payoff: API errors become predictable, debuggable, and easier for consumers to integrate.

If your API still mixes ad-hoc error shapes, standardising on `ProblemDetails` is a quick win.
