---
title: Primary Constructors in C# 12
date: 2026-05-05
tags: csharp, dotnet, tutorial
image: primary-constructors-in-csharp-12.png
---

C# 12 added primary constructors to classes and structs — a feature records had since C# 9, finally extended to the rest of the type system. If you've spent time writing boilerplate constructor bodies that just assign injected services to private fields, primary constructors are going to feel like a breath of fresh air.

## What's the Problem They Solve?

Before C# 12, a typical service class looked like this:

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly IEmailService _emailService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository repository,
        IEmailService emailService,
        ILogger<OrderService> logger)
    {
        _repository = repository;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task PlaceOrderAsync(Order order)
    {
        _logger.LogInformation("Placing order {OrderId}", order.Id);
        await _repository.SaveAsync(order);
        await _emailService.SendConfirmationAsync(order);
    }
}
```

That's three fields, three constructor parameters, and three assignments — all saying the same thing three times. For a class with five or six dependencies, this gets tedious fast.

## Primary Constructors to the Rescue

With primary constructors, you declare parameters directly on the class declaration, and they're available throughout the entire class body:

```csharp
public class OrderService(
    IOrderRepository repository,
    IEmailService emailService,
    ILogger<OrderService> logger)
{
    public async Task PlaceOrderAsync(Order order)
    {
        logger.LogInformation("Placing order {OrderId}", order.Id);
        await repository.SaveAsync(order);
        await emailService.SendConfirmationAsync(order);
    }
}
```

Same behaviour, a lot less ceremony. The parameters `repository`, `emailService`, and `logger` are in scope for every method, property, and field initialiser in the class. The DI container wires them up exactly as before — the constructor still exists, it's just implicit now.

## Parameters Aren't Automatically Fields

Here's the important thing to understand: primary constructor parameters are *not* automatically promoted to fields. They're captured by the class, but only as long as the class needs them. The compiler decides whether to create a backing field or not based on how you use the parameter.

If you only reference the parameter in field initialisers, the compiler may not generate a field at all. If you reference it in methods, it'll create a hidden synthesised field.

This matters for a few reasons. First, if you want to *mutate* a parameter (unusual, but possible), you can't — the synthesised backing field is read-only. Second, if you want explicit control over the field (say, you need to expose it or null-check on assignment), you need to declare it yourself:

```csharp
public class OrderService(IOrderRepository repository)
{
    // Explicitly create a named field if you need it
    private readonly IOrderRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public Task<Order?> GetAsync(int id) => _repository.GetAsync(id);
}
```

In this example `repository` is only used in the field initialiser, so the compiler captures it there. `_repository` is the real field. This pattern is handy when you want null checks or other validation on assignment.

## Works Great with Dependency Injection

The most common use case in ASP.NET Core is injecting services. Primary constructors make lean service classes even leaner:

```csharp
public class ProductCatalogService(
    IProductRepository products,
    ICacheService cache,
    ILogger<ProductCatalogService> logger)
{
    public async Task<IEnumerable<Product>> GetFeaturedAsync()
    {
        const string cacheKey = "featured-products";

        if (await cache.TryGetAsync<IEnumerable<Product>>(cacheKey) is { } cached)
        {
            logger.LogDebug("Cache hit for featured products");
            return cached;
        }

        var featured = await products.GetFeaturedAsync();
        await cache.SetAsync(cacheKey, featured, TimeSpan.FromMinutes(5));
        return featured;
    }
}
```

Register it as usual — DI doesn't care whether you wrote the constructor explicitly or used the primary constructor syntax:

```csharp
builder.Services.AddScoped<ProductCatalogService>();
```

## Using Them with Additional Constructors

You can still define additional constructors alongside a primary constructor. The catch is that every additional constructor must explicitly call the primary constructor using `this(...)`:

```csharp
public class ReportGenerator(ITemplateEngine engine, IDataSource dataSource)
{
    // Additional constructor with a default data source
    public ReportGenerator(ITemplateEngine engine)
        : this(engine, new DefaultDataSource())
    {
    }

    public string Generate(string templateName)
    {
        var data = dataSource.Fetch();
        return engine.Render(templateName, data);
    }
}
```

This keeps the primary constructor as the canonical path — all roads lead through it, so the parameters are always initialised.

## Classes vs Records vs Structs

Records had primary constructors first, but they work differently:

```csharp
// Record: parameters become init-only public properties automatically
public record Point(double X, double Y);

// Class: parameters are just parameters — no auto-properties
public class Point(double x, double y)
{
    public double X { get; } = x;
    public double Y { get; } = y;
}
```

For records, a primary constructor parameter `X` becomes a public `{ get; init; }` property named `X`. For classes and structs, that doesn't happen — you have to declare properties yourself if you want them. It's a meaningful difference that catches people out when they first switch between the two.

Structs work exactly like classes with primary constructors:

```csharp
public struct Colour(byte r, byte g, byte b)
{
    public byte R { get; } = r;
    public byte G { get; } = g;
    public byte B { get; } = b;

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
```

## Gotcha: Don't Reuse Parameter Names in the Class Body

Primary constructor parameters share the same name space as the rest of the class. If you accidentally shadow a parameter with a local variable or property of the same name, you'll get unexpected behaviour or a compiler warning:

```csharp
public class Processor(int timeout)
{
    // Warning: local 'timeout' shadows the primary constructor parameter
    public void Process()
    {
        int timeout = 30; // Don't do this — use a different name
        DoWork(timeout);
    }
}
```

The compiler will warn you, but it's still worth being deliberate about naming. A common convention is to keep primary constructor parameters in camelCase (they're not fields) and choose names that don't collide with local variables you'd naturally reach for.

## When to Use Them

Primary constructors shine when:

- You're injecting services and just storing them for later — the boilerplate reduction is real
- You're writing value-like classes with a small number of initialisation parameters
- You're working with structs where constructor verbosity is particularly painful

They're less obviously helpful when:

- You need custom logic on each assignment (null checks, validation) — you'll end up declaring explicit fields anyway
- The type has multiple overloaded constructors with very different shapes — the "all constructors must call the primary" rule gets awkward
- The parameters need to be exposed as public members — records do this automatically, classes don't

## Wrapping Up

Primary constructors in C# 12 cut through a lot of ceremony, especially in the dependency-injection-heavy code that's common in ASP.NET Core applications. The important things to keep in mind:

- Parameters are available anywhere in the class body, but aren't automatically fields
- Declare an explicit field if you need null validation, mutation, or a named property
- Records still promote parameters to properties automatically — classes and structs don't
- All non-primary constructors must chain to the primary with `this(...)`

If you're still writing the three-lines-per-dependency pattern in service classes, give primary constructors a shot. The reduction in noise makes the actual logic of the class stand out more clearly.
