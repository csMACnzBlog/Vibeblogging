---
title: CQRS with MediatR in .NET
date: 2026-04-18
tags: aspnetcore, dotnet, csharp, architecture, tutorial
image: cqrs-with-mediatr-in-dotnet.png
---

Most APIs start out simple: a controller calls a service, the service calls a repository, done. But as apps grow you end up with fat service classes doing everything — reading, writing, validating, orchestrating — and it gets messy fast.

CQRS (Command Query Responsibility Segregation) is the idea that reading data and changing data are fundamentally different concerns, so they should be handled separately. MediatR is a small library that makes implementing this pattern in .NET a pleasure.

## What CQRS Actually Means

The core idea is straightforward: a **query** reads data and returns something. A **command** changes state and typically returns nothing (or just an id/status). You keep those paths completely separate.

That's it. You don't need separate databases, event sourcing, or microservices to benefit from this. The value is in the clarity and testability you get from keeping reads and writes apart.

## Installing MediatR

```bash
dotnet add package MediatR
```

Register it in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<Program>());

var app = builder.Build();
```

That one line scans the assembly and registers every handler it finds. No manual wiring.

## Your First Query

A query is just a record (or class) that implements `IRequest<TResponse>`. You pair it with a handler that implements `IRequestHandler<TRequest, TResponse>`.

```csharp
// The query — what you're asking for
public record GetProductQuery(int Id) : IRequest<ProductDto?>;

// The handler — how to answer it
public class GetProductHandler : IRequestHandler<GetProductQuery, ProductDto?>
{
    private readonly IProductRepository _repository;

    public GetProductHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductDto?> Handle(
        GetProductQuery request,
        CancellationToken cancellationToken)
    {
        var product = await _repository.GetByIdAsync(request.Id, cancellationToken);
        return product is null ? null : new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

In your endpoint or controller, you inject `IMediator` and send the query:

```csharp
app.MapGet("/products/{id:int}", async (int id, IMediator mediator) =>
{
    var result = await mediator.Send(new GetProductQuery(id));
    return result is null ? Results.NotFound() : Results.Ok(result);
});
```

MediatR finds the matching handler and calls it. The endpoint doesn't know or care about repositories, databases, or anything else.

## Your First Command

Commands follow the same pattern. They implement `IRequest` (no response) or `IRequest<T>` if you need to return something like a new id.

```csharp
// The command
public record CreateProductCommand(string Name, decimal Price) : IRequest<int>;

// The handler
public class CreateProductHandler : IRequestHandler<CreateProductCommand, int>
{
    private readonly IProductRepository _repository;

    public CreateProductHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> Handle(
        CreateProductCommand request,
        CancellationToken cancellationToken)
    {
        var product = new Product { Name = request.Name, Price = request.Price };
        await _repository.AddAsync(product, cancellationToken);
        return product.Id;
    }
}
```

And the endpoint:

```csharp
app.MapPost("/products", async (CreateProductCommand command, IMediator mediator) =>
{
    var id = await mediator.Send(command);
    return Results.Created($"/products/{id}", new { id });
});
```

## Pipeline Behaviours

This is where MediatR gets really interesting. You can inject cross-cutting concerns into the pipeline — validation, logging, performance tracking — without touching any of your handlers.

A behaviour wraps every request that passes through:

```csharp
public class LoggingBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled {RequestName}", typeof(TRequest).Name);
        return response;
    }
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddTransient(
    typeof(IPipelineBehavior<,>),
    typeof(LoggingBehaviour<,>));
```

Now every command and query gets logged, without changing a single handler. You can stack multiple behaviours — logging, then validation, then performance tracking — and they run in registration order.

## Validation with FluentValidation

A common pattern is to pair MediatR with FluentValidation in a validation behaviour:

```csharp
public class ValidationBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

Define a validator for your command:

```csharp
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Price).GreaterThan(0);
    }
}
```

The behaviour picks up all registered validators automatically. Valid requests pass through. Invalid ones throw before the handler ever runs.

## Testing Is a Joy

One of the biggest wins with this pattern is testability. Because each handler is a small, focused class, testing it means testing one thing:

```csharp
public class GetProductHandlerTests
{
    [Fact]
    public async Task Returns_null_when_product_not_found()
    {
        var repository = Substitute.For<IProductRepository>();
        repository.GetByIdAsync(99, default).Returns((Product?)null);

        var handler = new GetProductHandler(repository);
        var result = await handler.Handle(new GetProductQuery(99), default);

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_dto_when_product_exists()
    {
        var repository = Substitute.For<IProductRepository>();
        repository.GetByIdAsync(1, default).Returns(
            new Product { Id = 1, Name = "Widget", Price = 9.99m });

        var handler = new GetProductHandler(repository);
        var result = await handler.Handle(new GetProductQuery(1), default);

        Assert.NotNull(result);
        Assert.Equal("Widget", result.Name);
    }
}
```

No mocking frameworks with complicated setups. No spinning up a whole API. Just: create handler, call `Handle`, assert.

## Keeping Handlers Small

The single most important rule when using MediatR: **keep handlers small**. If a handler is doing more than one logical thing, it's a signal to split it or extract a service.

A handler that creates a product, sends a welcome email, updates a counter, and clears a cache is just a fat service method with a different name. The pattern only helps if you respect the single-responsibility principle within each handler.

## Wrapping Up

CQRS with MediatR is one of those patterns that sounds complicated but makes your code noticeably cleaner once you try it. Queries and commands stay separate, cross-cutting concerns live in behaviours, and each handler is small enough to read in 30 seconds.

It's not the right tool for every project — a tiny CRUD API probably doesn't need it. But once your application has more than a handful of operations and you're feeling the pain of service classes doing too much, this is a very clean way out.
