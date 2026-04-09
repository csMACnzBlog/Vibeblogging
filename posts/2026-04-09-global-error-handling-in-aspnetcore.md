---
title: Global Error Handling in ASP.NET Core
date: 2026-04-09
tags: aspnetcore, dotnet, csharp, tutorial
image: global-error-handling-in-aspnetcore.png
---

Every ASP.NET Core app will encounter unhandled exceptions. A database goes away, a downstream service times out, someone passes a value your code didn't anticipate. How you handle those exceptions — and what you send back to clients — matters a lot for both the developer experience and the quality of your API.

The naive approach is `try/catch` in every action method. That works, but it's noisy, repetitive, and easy to miss. The better approach is to handle exceptions in one place, consistently, and return structured error responses your clients can actually use.

## The Problem with Scattered Error Handling

Here's what it looks like when you handle errors per-endpoint:

```csharp
[HttpGet("{id}")]
public async Task<IActionResult> Get(int id)
{
    try
    {
        var product = await _repository.GetByIdAsync(id);
        if (product is null)
            return NotFound();

        return Ok(product);
    }
    catch (DatabaseException ex)
    {
        _logger.LogError(ex, "Database error fetching product {Id}", id);
        return StatusCode(500, "Something went wrong.");
    }
}
```

Multiply that pattern across every endpoint and you've got a maintenance problem. The error responses are inconsistent, the logging is ad hoc, and adding cross-cutting concerns (like correlation IDs) means touching every handler.

## UseExceptionHandler: Middleware Approach

ASP.NET Core's built-in exception handling middleware catches unhandled exceptions and runs a secondary pipeline. It's been around a while and it's the simplest centralised option.

```csharp
var app = builder.Build();

app.UseExceptionHandler("/error");
```

Then add an endpoint at `/error` that constructs the response:

```csharp
app.Map("/error", (HttpContext context) =>
{
    var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
    var exception = exceptionFeature?.Error;

    return Results.Problem(
        title: "An error occurred",
        detail: exception?.Message,
        statusCode: StatusCodes.Status500InternalServerError);
});
```

This works and it's fine for simple cases. The downside is that routing to `/error` introduces a second request lifecycle, and it can be awkward when you want to handle different exception types differently.

## IExceptionHandler: The Modern Way

.NET 8 added `IExceptionHandler`, which gives you a clean, DI-friendly interface for handling exceptions:

```csharp
public interface IExceptionHandler
{
    ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken);
}
```

Return `true` to indicate you've handled the exception (the pipeline stops). Return `false` to pass it to the next handler. You can register multiple handlers and they run in order.

Here's a handler that maps known domain exceptions to appropriate HTTP responses:

```csharp
public class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
            return false;

        _logger.LogWarning(
            domainException,
            "Domain exception: {Message}",
            domainException.Message);

        httpContext.Response.StatusCode = domainException.StatusCode;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Title = domainException.Title,
                Detail = domainException.Message,
                Status = domainException.StatusCode,
            },
            cancellationToken);

        return true;
    }
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
```

The order of `AddExceptionHandler` calls matters — handlers are tried in registration order. Put specific handlers before catch-all ones.

## ProblemDetails: Standardised Error Responses

[RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) defines a standard shape for HTTP error responses. ASP.NET Core's `ProblemDetails` type implements it:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Not Found",
  "status": 404,
  "detail": "Product with ID 42 was not found.",
  "instance": "/products/42"
}
```

Using `Results.Problem()` or `TypedResults.Problem()` in Minimal APIs, or `ControllerBase.Problem()` in controllers, both produce this shape. Once `AddProblemDetails()` is registered, even unhandled exceptions produce structured responses instead of bare HTML error pages.

A catch-all handler that logs unexpected exceptions and returns a 500:

```csharp
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
            },
            cancellationToken);

        return true;
    }
}
```

This is a reasonable baseline. It doesn't leak internal error details to callers but still logs the full exception for debugging.

## Domain Exceptions

To make this pattern clean, define a base exception class your domain can throw:

```csharp
public class DomainException : Exception
{
    public int StatusCode { get; }
    public string Title { get; }

    public DomainException(string title, string message, int statusCode = 400)
        : base(message)
    {
        Title = title;
        StatusCode = statusCode;
    }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string resourceName, object id)
        : base("Not Found", $"{resourceName} with ID '{id}' was not found.", 404)
    {
    }
}

public class ConflictException : DomainException
{
    public ConflictException(string message)
        : base("Conflict", message, 409)
    {
    }
}
```

Now domain code can throw typed exceptions without knowing anything about HTTP:

```csharp
public async Task<Product> GetByIdAsync(int id)
{
    var product = await _dbContext.Products.FindAsync(id);
    if (product is null)
        throw new NotFoundException(nameof(Product), id);

    return product;
}
```

The handler converts that to a 404 with a structured response. The service layer stays clean.

## Adding a Correlation ID

One useful addition is including a correlation ID in error responses so you can trace a client's error report back to your logs. You can add it as an extension on `ProblemDetails`:

```csharp
public class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
            return false;

        _logger.LogWarning(
            domainException,
            "Domain exception: {Message}",
            domainException.Message);

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = domainException.StatusCode;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Title = domainException.Title,
                Detail = domainException.Message,
                Status = domainException.StatusCode,
                Extensions = { ["traceId"] = traceId },
            },
            cancellationToken);

        return true;
    }
}
```

Clients that get a 500 can quote the `traceId`, and you can find the matching log entry without any guesswork.

## Putting It Together

Here's the registration in full:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(); // or AddEndpointsApiExplorer for Minimal APIs

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

// rest of middleware and routes...
```

One `UseExceptionHandler()` call, no path argument needed when using `IExceptionHandler`. The handlers you registered take care of everything.

## Worth Knowing

A few things that tend to catch people out:

**Development vs production**: In development, you probably want to see the full exception stack trace. `UseDeveloperExceptionPage()` is the tool for that, and it should only be active in development:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}
```

**Exceptions in middleware**: `IExceptionHandler` and `UseExceptionHandler` catch exceptions that happen during request handling — that means exceptions thrown in middleware further up the pipeline won't be caught. Handle exceptions in your own middleware explicitly.

**Don't swallow errors**: It's tempting to return 200 for everything and wrap errors in a response envelope. Resist that temptation. HTTP status codes exist precisely for this, and clients (including your own frontend) know how to handle them.

## One Place, Done Right

Centralised error handling isn't glamorous, but it's one of those things you'll thank yourself for later. Define your exception hierarchy, register your handlers, and keep your endpoint code focused on the happy path. When something goes wrong, you'll get consistent responses, good logs, and no surprises.
