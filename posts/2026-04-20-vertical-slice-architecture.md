---
title: Vertical Slice Architecture
date: 2026-04-20
tags: dotnet, csharp, architecture, aspnetcore
image: vertical-slice-architecture.png
---

If you've spent time working in layered architecture — controllers, services, repositories, all the way down — you've probably felt the friction. Adding a feature means touching four or five layers, threading a change through each one. The layers were meant to separate concerns, but they often just separate related code.

Vertical Slice Architecture is a different way to think about structure. Instead of organising code by technical layer, you organise it by *feature*. Each feature is a self-contained slice that owns everything it needs — from the HTTP endpoint to the database query.

## The Problem with Layers

Here's the thing about horizontal layers: they group code that *looks* similar, not code that *belongs* together. A `ProductService` class sits next to `OrderService` and `UserService`, even though they're completely unrelated except for the fact that they're all "services".

When you add a new feature, you update the controller, then jump to the service layer, then jump to the repository layer, then maybe the data access layer. These are often in different namespaces, different files, sometimes even different projects. The feature is *scattered* across the codebase.

The result: cohesion within layers, coupling across them. Changes to one feature have an uncanny ability to affect others.

## Slices Instead of Layers

Vertical slices turn this inside out. A slice is a feature. It contains everything that feature needs — the request, the handler, validation, the database call, the response. It's a unit you can reason about, test, and change without touching anything else.

The most natural way to implement this in .NET is to combine it with MediatR-style handlers (we covered CQRS with MediatR recently). Each slice is a command or query, its handler, and its associated types.

Here's what a feature folder looks like:

```
Features/
├── Products/
│   ├── GetProduct.cs
│   ├── CreateProduct.cs
│   └── UpdateProductPrice.cs
├── Orders/
│   ├── PlaceOrder.cs
│   └── CancelOrder.cs
```

Compare that to:

```
Controllers/
├── ProductsController.cs
├── OrdersController.cs
Services/
├── ProductService.cs
├── OrderService.cs
Repositories/
├── ProductRepository.cs
├── OrderRepository.cs
```

The second layout tells you about the *shape* of the code. The first tells you about the *behaviour*.

## Building a Slice

Each file in a feature folder is a vertical slice. Here's a complete `GetProduct` slice:

```csharp
public static class GetProduct
{
    public record Query(int Id) : IRequest<Response?>;

    public record Response(int Id, string Name, decimal Price);

    public class Handler : IRequestHandler<Query, Response?>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Response?> Handle(Query request, CancellationToken ct)
        {
            var product = await _db.Products
                .Where(p => p.Id == request.Id)
                .Select(p => new Response(p.Id, p.Name, p.Price))
                .FirstOrDefaultAsync(ct);

            return product;
        }
    }
}
```

The query, the response, and the handler all live in one file. There's no `ProductService` to navigate to, no `ProductRepository` to dig through. The entire read path for "get a product" is right here.

The endpoint registers the slice:

```csharp
app.MapGet("/api/products/{id:int}", async (int id, IMediator mediator) =>
{
    var result = await mediator.Send(new GetProduct.Query(id));
    return result is null ? Results.NotFound() : Results.Ok(result);
});
```

## A Write Slice

Write operations follow the same pattern. Here's `CreateProduct`, including validation:

```csharp
public static class CreateProduct
{
    public record Command(string Name, decimal Price) : IRequest<int>;

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(x => x.Price)
                .GreaterThan(0);
        }
    }

    public class Handler : IRequestHandler<Command, int>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db)
        {
            _db = db;
        }

        public async Task<int> Handle(Command request, CancellationToken ct)
        {
            var product = new Product
            {
                Name = request.Name,
                Price = request.Price
            };

            _db.Products.Add(product);
            await _db.SaveChangesAsync(ct);

            return product.Id;
        }
    }
}
```

Again: one file, one feature. The validator lives next to the thing it validates, not in some shared `Validators/` folder that grows to hundreds of entries.

## Cross-Cutting Concerns

The obvious question: if each slice is independent, where do shared concerns go? Logging, validation, authorisation — these aren't feature-specific. The answer is MediatR pipeline behaviours. They sit between the dispatcher and the handler, applied to all requests:

```csharp
public class ValidationBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(e => e != null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

Register it once and it runs for every request:

```csharp
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
});
```

Now every slice with a validator gets validation automatically, without any slice needing to call the validator explicitly. The slice stays focused on its feature; the pipeline handles the plumbing.

## Testing Slices in Isolation

This is where vertical slices really shine. Each handler is a plain C# class with explicit dependencies — easy to instantiate, easy to test:

```csharp
public class GetProductTests
{
    [Fact]
    public async Task Returns_null_when_product_does_not_exist()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        var handler = new GetProduct.Handler(db);

        var result = await handler.Handle(new GetProduct.Query(99), default);

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_product_when_it_exists()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        db.Products.Add(new Product { Id = 1, Name = "Widget", Price = 9.99m });
        await db.SaveChangesAsync();

        var handler = new GetProduct.Handler(db);

        var result = await handler.Handle(new GetProduct.Query(1), default);

        Assert.NotNull(result);
        Assert.Equal("Widget", result.Name);
        Assert.Equal(9.99m, result.Price);
    }
}
```

No mocking framework, no abstract service to stub out. Just create the handler, call it, check the result. Compare this to testing a feature in a layered architecture where you'd often need to mock three or four collaborators just to reach the code under test.

## When Does Sharing Make Sense?

Vertical slices aren't allergic to sharing — they're just deliberate about it. You should share things that are genuinely cross-cutting:

- **Domain entities**: `Product`, `Order`, `Customer` — these represent your domain model, not a layer.
- **Database context**: Shared infrastructure, not feature logic.
- **Pipeline behaviours**: Logging, validation, exception handling.
- **Common utilities**: Date helpers, extension methods, constants.

What you shouldn't share is business logic. If two features seem to need the same service method, resist the temptation to extract it. Often it's a coincidence — the features happen to look similar now, but they'll diverge as requirements change. Duplicate it and let each slice own its own logic.

This is the key mindset shift: in layered architecture, duplication is the enemy. In vertical slices, *coupling* is the enemy. A little duplication between two slices is far less harmful than the coupling that comes from sharing code that will change for different reasons.

## The Trade-Off

Vertical slices aren't a free lunch. Large teams need discipline to keep slices genuinely independent and avoid the temptation to grow a "shared" folder until it becomes a sixth layer. The `Features/` folder can grow quickly — a hundred-feature application has a hundred files there.

The sweet spot is most obvious in complex business domains: things like e-commerce, insurance, healthcare software. Anywhere you have many distinct workflows that happen to share some infrastructure. If your application is mostly CRUD with no real domain logic, vertical slices may be more structure than you need.

But if you've ever added a simple feature and found yourself touching a dozen files across multiple layers, it's worth a try. Pick one new feature and build it as a slice. See how it feels to have everything in one place.

## Wrapping Up

Vertical Slice Architecture flips the usual organising principle. Instead of grouping by technical layer, you group by feature. Each slice owns its own request, handler, validation, and data access — everything it needs, nothing it doesn't.

Combined with MediatR for dispatching and FluentValidation in pipeline behaviours, you get a structure that scales naturally with the number of features rather than the size of the team. Each piece of the application is easy to find, easy to change, and easy to test in isolation.

It won't replace clean architecture everywhere, but for complex business domains with lots of distinct workflows, vertical slices are one of the most maintainable approaches I've used.
