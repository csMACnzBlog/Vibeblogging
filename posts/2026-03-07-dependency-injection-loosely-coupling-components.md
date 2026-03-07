---
title: "Dependency Injection - Loosely Coupled Components"
date: 2026-03-07
tags: csharp, design-patterns, dependency-injection, dotnet, architecture
image: dependency-injection-loosely-coupling-components.png
---

At the end of the [Chain of Responsibility post](chain-of-responsibility-passing-requests-through-handlers.html) I mentioned that every pattern in this series has depended on one thing: injecting interfaces rather than creating concrete types directly. We've been doing it all along. The Repository Pattern gave us `IOrderRepository`. The Decorator wrapped it. The Chain of Responsibility built a pipeline from injected handlers. But we've glossed over *how* all of that wiring happens — and why it matters.

That's what Dependency Injection is about. It's the pattern that ties everything together. By the end of this post, you'll understand not just how DI works, but why it's the foundation that makes every other pattern in this series actually usable in a real application.

This is also the final post in the series. We'll wrap up by showing you how all the pieces fit together.

## What Is Dependency Injection?

Dependency Injection is a technique where an object receives its dependencies from the outside rather than creating them itself. The name sounds more intimidating than the concept.

Here's the one-sentence version: **instead of calling `new` inside your class, you accept what you need through the constructor.** That's it. Everything else — DI containers, service lifetimes, factory registrations — is just tooling built around that core idea.

It's an application of the [Dependency Inversion Principle](solid-principles-foundation-of-good-design.html) from SOLID: high-level modules shouldn't depend on low-level modules; both should depend on abstractions. DI is the mechanism that makes that principle practical.

## The Problem: Manual Object Creation

Let's start with the anti-pattern. Here's an `OrderService` that creates its own dependencies:

```csharp
public class OrderService
{
    private readonly SqlOrderRepository _repository;
    private readonly EmailNotificationService _emailService;

    public OrderService()
    {
        var connectionFactory = new SqlConnectionFactory("Server=...;Database=Orders;");
        _repository = new SqlOrderRepository(connectionFactory);
        _emailService = new EmailNotificationService("smtp.example.com", 587);
    }

    public async Task<Order?> GetOrderAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task PlaceOrderAsync(Order order)
    {
        await _repository.SaveAsync(order);
        await _emailService.SendConfirmationAsync(order.CustomerEmail, order.Id);
    }
}
```

This looks reasonable until you try to test it. There's no way to run `OrderService` in a unit test without a real SQL Server and a real SMTP server. Every test becomes an integration test. Your CI pipeline needs a database. Your tests are slow. Tests start failing because of network issues that have nothing to do with the logic you're testing.

There are subtler problems too. `OrderService` knows the connection string format for SQL Server. It knows the SMTP port for your email provider. Change your email provider? Edit `OrderService`. Scale up to multiple services that all create their own `EmailNotificationService` instances? Good luck keeping configuration consistent.

This is tight coupling. `OrderService` is directly coupled to `SqlOrderRepository`, `SqlConnectionFactory`, and `EmailNotificationService`. You can't use one without the others.

## Constructor Injection

The fix is to stop calling `new` inside the class and accept dependencies through the constructor instead:

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository repository,
        INotificationService notificationService,
        ILogger<OrderService> logger)
    {
        _repository = repository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Order?> GetOrderAsync(int id)
    {
        _logger.LogInformation("Fetching order {OrderId}", id);
        return await _repository.GetByIdAsync(id);
    }

    public async Task PlaceOrderAsync(Order order)
    {
        await _repository.SaveAsync(order);
        await _notificationService.SendConfirmationAsync(order.CustomerEmail, order.Id);
        _logger.LogInformation("Order {OrderId} placed successfully", order.Id);
    }
}
```

Notice what changed. `OrderService` no longer knows anything about SQL Server or SMTP. It doesn't know *how* to fetch orders or *how* to send notifications. It only knows *that* it needs something that can fetch orders and something that can send notifications. The interfaces define the contract. The implementations are someone else's problem.

This is the Dependency Inversion Principle in action. `OrderService` depends on `IOrderRepository` — an abstraction. It doesn't depend on `SqlOrderRepository` — a concretion. You can swap implementations freely, and `OrderService` doesn't care.

## Manual DI (No Container)

You don't need a DI container to use Dependency Injection. You can wire everything up manually in your `Program.cs` or an application entry point:

```csharp
// Composition root — this is where you assemble the application
var connectionFactory = new SqlConnectionFactory(configuration.GetConnectionString("Orders"));
var emailClient = new SmtpEmailClient(configuration["Email:Host"], int.Parse(configuration["Email:Port"]));

IOrderRepository repository = new SqlOrderRepository(connectionFactory);
INotificationService notificationService = new EmailNotificationService(emailClient);
var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OrderService>();

var orderService = new OrderService(repository, notificationService, logger);
```

This is called the **Composition Root** — the single place in your application where you construct the entire object graph. All the `new` calls live here, not scattered across your domain classes. This keeps your business logic clean while still giving you full control over what gets created.

Manual wiring is great for small applications or when you want to understand exactly what's happening. For larger applications, it becomes tedious — which is why DI containers exist.

## dotNET's Built-In DI Container

.NET ships with a built-in DI container in `Microsoft.Extensions.DependencyInjection`. You've been using it every time you've written a `WebApplication.CreateBuilder()` call in ASP.NET Core. It's already there.

Registrations go in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register your services
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
builder.Services.AddScoped<INotificationService, EmailNotificationService>();
builder.Services.AddScoped<OrderService>();

// The framework handles ILogger<T> automatically
builder.Services.AddLogging();

var app = builder.Build();
```

When ASP.NET Core creates a controller or minimal API handler that needs an `OrderService`, the container sees that `OrderService` requires `IOrderRepository`, `INotificationService`, and `ILogger<OrderService>`. It resolves each dependency in turn, constructing the full object graph automatically. You never call `new` for any of these types.

This is genuinely powerful. You describe *what* you need, and the container figures out *how* to build it. Add a new dependency to `OrderService`? Add it to the constructor and register the implementation. The container handles the rest.

## Service Lifetimes

One of the most important decisions you'll make when registering services is their lifetime. .NET's container offers three:

**Transient** — a new instance every time the service is requested:

```csharp
builder.Services.AddTransient<IOrderValidator, OrderValidator>();
```

Use transient for lightweight, stateless services. Every component that depends on `IOrderValidator` gets its own fresh instance. Safe for concurrent use because there's no shared state.

**Scoped** — one instance per request (or per scope):

```csharp
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
builder.Services.AddScoped<IUnitOfWork, DbUnitOfWork>();
```

Scoped is the right choice for database connections and unit of work objects. Everything that participates in a single HTTP request shares the same instance, which means they share the same database transaction. When the request ends, the scope is disposed and the connection is released.

**Singleton** — one instance for the entire application lifetime:

```csharp
builder.Services.AddSingleton<IOrderCache, InMemoryOrderCache>();
builder.Services.AddSingleton<IConfiguration>(configuration);
```

Singletons are appropriate for expensive-to-create, thread-safe services. In-memory caches, configuration objects, and HTTP clients (via `IHttpClientFactory`) are common examples.

Choosing the wrong lifetime is a common source of bugs — we'll cover that in the mistakes section.

## Testability: the Real Payoff

Here's why constructor injection matters so much: it makes your classes trivially testable. You can pass in any implementation of the interface — including fakes and mocks — without the class knowing the difference.

Let's write a test for `PlaceOrderAsync`. Without DI, we'd need a real database and a real email server. With DI, we use a fake repository:

```csharp
public class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<int, Order> _orders = new();
    private int _nextId = 1;

    public Task<Order?> GetByIdAsync(int id)
    {
        _orders.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task SaveAsync(Order order)
    {
        order.Id = _nextId++;
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }
}

public class FakeNotificationService : INotificationService
{
    public List<(string Email, int OrderId)> SentNotifications { get; } = new();

    public Task SendConfirmationAsync(string email, int orderId)
    {
        SentNotifications.Add((email, orderId));
        return Task.CompletedTask;
    }
}
```

Now the test:

```csharp
public class OrderServiceTests
{
    [Fact]
    public async Task PlaceOrderAsync_SavesOrderAndSendsNotification()
    {
        // Arrange
        var repository = new FakeOrderRepository();
        var notificationService = new FakeNotificationService();
        var logger = new NullLogger<OrderService>();

        var service = new OrderService(repository, notificationService, logger);

        var order = new Order
        {
            CustomerEmail = "alice@example.com",
            Items = new List<OrderItem>
            {
                new() { ProductId = 42, Quantity = 2, UnitPrice = 9.99m }
            }
        };

        // Act
        await service.PlaceOrderAsync(order);

        // Assert
        var savedOrder = await repository.GetByIdAsync(order.Id);
        Assert.NotNull(savedOrder);
        Assert.Single(notificationService.SentNotifications);
        Assert.Equal("alice@example.com", notificationService.SentNotifications[0].Email);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenRepositoryFails_DoesNotSendNotification()
    {
        // Arrange
        var repository = new FailingOrderRepository(); // always throws
        var notificationService = new FakeNotificationService();
        var logger = new NullLogger<OrderService>();

        var service = new OrderService(repository, notificationService, logger);

        // Act & Assert
        await Assert.ThrowsAsync<DatabaseException>(
            () => service.PlaceOrderAsync(new Order { CustomerEmail = "bob@example.com" }));

        Assert.Empty(notificationService.SentNotifications);
    }
}
```

These tests run in milliseconds. No database. No network. No flaky infrastructure. You're testing the logic of `OrderService` in complete isolation. That's the payoff of constructor injection.

If you prefer mocking frameworks over hand-written fakes, the same injection point works with Moq or NSubstitute:

```csharp
[Fact]
public async Task GetOrderAsync_LogsOrderId()
{
    // Arrange
    var repository = Substitute.For<IOrderRepository>();
    var notificationService = Substitute.For<INotificationService>();
    var logger = Substitute.For<ILogger<OrderService>>();

    repository.GetByIdAsync(99).Returns(new Order { Id = 99, CustomerEmail = "test@example.com" });

    var service = new OrderService(repository, notificationService, logger);

    // Act
    var result = await service.GetOrderAsync(99);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(99, result.Id);
}
```

The interface is the seam. The seam is what makes testing possible.

## Wiring Up the Series Patterns

Now for the fun part. We've built up a whole catalogue of patterns across this series. Let's wire them all together using DI and see how clean the composition looks.

Here's what we're composing:

- `SqlOrderRepository` — the real data access, from the [Repository Pattern post](repository-pattern-abstracting-data-access.html)
- `CachingOrderRepository` — wraps the above, from the [Decorator Pattern post](decorator-pattern-adding-behavior-without-modification.html)
- `LoggingOrderRepository` — wraps the caching decorator, also from the Decorator post
- An `OrderPipeline` with Chain of Responsibility handlers for validation and processing, from the [Chain of Responsibility post](chain-of-responsibility-passing-requests-through-handlers.html)
- Multiple `IOrderProcessingStrategy` implementations selected at runtime, from the [Strategy Pattern post](strategy-pattern-swapping-algorithms-at-runtime.html)
- `OrderService` sitting on top of all of it

Here's the full DI registration:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Infrastructure
builder.Services.AddSingleton<IDbConnectionFactory>(sp =>
    new SqlConnectionFactory(builder.Configuration.GetConnectionString("Orders")!));

builder.Services.AddMemoryCache();

// Repository with decorator stack
// Register the real implementation under its concrete type
builder.Services.AddScoped<SqlOrderRepository>();

// Register the decorator chain: SQL -> Caching -> Logging
builder.Services.AddScoped<IOrderRepository>(sp =>
{
    var inner = sp.GetRequiredService<SqlOrderRepository>();
    var cache = sp.GetRequiredService<IMemoryCache>();
    var cachingLogger = sp.GetRequiredService<ILogger<CachingOrderRepository>>();
    var loggingLogger = sp.GetRequiredService<ILogger<LoggingOrderRepository>>();

    IOrderRepository cached = new CachingOrderRepository(inner, cache, cachingLogger);
    return new LoggingOrderRepository(cached, loggingLogger);
});

// Chain of Responsibility handlers (order matters)
builder.Services.AddScoped<IOrderPipelineHandler, OrderValidationHandler>();
builder.Services.AddScoped<IOrderPipelineHandler, InventoryCheckHandler>();
builder.Services.AddScoped<IOrderPipelineHandler, FraudDetectionHandler>();
builder.Services.AddScoped<IOrderPipelineHandler, OrderPersistenceHandler>();
builder.Services.AddScoped<OrderPipeline>();

// Strategy pattern implementations
builder.Services.AddScoped<StandardOrderStrategy>();
builder.Services.AddScoped<ExpressOrderStrategy>();
builder.Services.AddScoped<SubscriptionOrderStrategy>();

// The strategy resolver uses the factory pattern to pick the right strategy
builder.Services.AddScoped<IOrderStrategyResolver, OrderStrategyResolver>();

// Application services
builder.Services.AddScoped<OrderService>();

builder.Services.AddControllers();
```

Look at how much work is happening in that registration, and how none of it leaks into `OrderService`. Here's what `OrderService` sees:

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly OrderPipeline _pipeline;
    private readonly IOrderStrategyResolver _strategyResolver;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository repository,
        OrderPipeline pipeline,
        IOrderStrategyResolver strategyResolver,
        ILogger<OrderService> logger)
    {
        _repository = repository;
        _pipeline = pipeline;
        _strategyResolver = strategyResolver;
        _logger = logger;
    }

    public async Task<OrderResult> ProcessOrderAsync(OrderRequest request)
    {
        _logger.LogInformation("Processing order for customer {CustomerId}", request.CustomerId);

        // The pipeline runs validation, inventory checks, fraud detection
        var context = new OrderContext(request);
        var pipelineResult = await _pipeline.ExecuteAsync(context);

        if (!pipelineResult.IsSuccess)
            return OrderResult.Failed(pipelineResult.Errors);

        // The strategy resolver picks StandardOrderStrategy, ExpressOrderStrategy, etc.
        var strategy = _strategyResolver.Resolve(request.OrderType);
        var order = await strategy.CreateOrderAsync(request);

        // The repository here is actually: LoggingRepository -> CachingRepository -> SqlRepository
        await _repository.SaveAsync(order);

        return OrderResult.Succeeded(order.Id);
    }
}
```

`OrderService.ProcessOrderAsync` is clean, readable, and entirely focused on the *what* rather than the *how*. It has no idea that:

- The repository logs every call and caches read results
- The pipeline runs four different validation steps
- The strategy selection is a factory lookup across three implementations

All of that complexity exists — but it's been pushed to the edges of the system, into the Composition Root, where it belongs.

## Factory Registrations for Runtime Selection

Sometimes you need more control over how a service is constructed. The [Factory Pattern](factory-pattern-hiding-object-creation.html) fits naturally into DI registrations.

The `IOrderStrategyResolver` from above might look like this:

```csharp
public class OrderStrategyResolver : IOrderStrategyResolver
{
    private readonly IServiceProvider _serviceProvider;

    public OrderStrategyResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IOrderProcessingStrategy Resolve(OrderType orderType)
    {
        return orderType switch
        {
            OrderType.Standard => _serviceProvider.GetRequiredService<StandardOrderStrategy>(),
            OrderType.Express => _serviceProvider.GetRequiredService<ExpressOrderStrategy>(),
            OrderType.Subscription => _serviceProvider.GetRequiredService<SubscriptionOrderStrategy>(),
            _ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unknown order type")
        };
    }
}
```

Notice that `OrderStrategyResolver` injects `IServiceProvider` — not `OrderService`. This is one of the legitimate uses of `IServiceProvider`. The resolver's *entire job* is to resolve services. It's infrastructure, not business logic.

This is the key distinction. Injecting `IServiceProvider` into a resolver or factory that exists specifically to do runtime resolution is fine. Injecting it into `OrderService` so you can call `GetRequiredService` on demand is the Service Locator anti-pattern. More on that in a moment.

## Keyed Services in dotNET 8+

.NET 8 added keyed services, which gives you another clean option for the strategy resolution problem:

```csharp
// Registration
builder.Services.AddKeyedScoped<IOrderProcessingStrategy, StandardOrderStrategy>("standard");
builder.Services.AddKeyedScoped<IOrderProcessingStrategy, ExpressOrderStrategy>("express");
builder.Services.AddKeyedScoped<IOrderProcessingStrategy, SubscriptionOrderStrategy>("subscription");
```

```csharp
// Resolution
public class OrderStrategyResolver : IOrderStrategyResolver
{
    private readonly IServiceProvider _serviceProvider;

    public OrderStrategyResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IOrderProcessingStrategy Resolve(OrderType orderType)
    {
        var key = orderType.ToString().ToLowerInvariant();
        return _serviceProvider.GetRequiredKeyedService<IOrderProcessingStrategy>(key);
    }
}
```

Or with direct injection using `[FromKeyedServices]`:

```csharp
public class ExpressOrderController : ControllerBase
{
    private readonly IOrderProcessingStrategy _strategy;

    public ExpressOrderController(
        [FromKeyedServices("express")] IOrderProcessingStrategy strategy)
    {
        _strategy = strategy;
    }
}
```

Keyed services are cleaner than the switch statement approach when the key is known at registration time.

## Common Mistakes and Best Practices

### Mistake 1: The Service Locator Anti-Pattern

Don't inject `IServiceProvider` into your domain classes to grab dependencies on demand:

```csharp
// DON'T DO THIS
public class OrderService
{
    private readonly IServiceProvider _serviceProvider;

    public OrderService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task PlaceOrderAsync(Order order)
    {
        // Hidden dependency — you can't see it from the constructor
        var repository = _serviceProvider.GetRequiredService<IOrderRepository>();
        await repository.SaveAsync(order);
    }
}
```

This defeats the whole purpose of DI. You've hidden your dependencies inside the method body. You can't tell what `OrderService` needs by looking at its constructor. Testing becomes harder, not easier, because you'd need to set up a full `IServiceProvider` to run a test.

Constructor injection makes dependencies explicit. Keep them explicit.

### Mistake 2: Captive Dependencies

This one is subtle and causes real production bugs. A captive dependency is when a longer-lived service holds a reference to a shorter-lived one.

The most common case: a singleton that depends on a scoped service.

```csharp
// DON'T DO THIS
builder.Services.AddSingleton<OrderCache>(); // singleton
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>(); // scoped

// OrderCache constructor:
public class OrderCache
{
    private readonly IOrderRepository _repository; // captured at startup!

    public OrderCache(IOrderRepository repository)
    {
        _repository = repository;
    }
}
```

`OrderCache` is a singleton — it's created once and lives forever. But `IOrderRepository` is scoped — it's supposed to be created per-request and disposed at the end of the request. When the singleton captures the scoped service, it prevents disposal and keeps the same instance alive for the entire app lifetime. Your database connections won't be released. You'll end up with connection pool exhaustion.

.NET's DI container will throw an `InvalidOperationException` at startup when it detects this in development mode (`ValidateScopes` is true by default for `WebApplication`). In production, this validation is off by default — which is exactly when you'll hit the bug.

The fix is straightforward: singletons should only depend on other singletons (or transients, which are stateless). If you genuinely need a scoped service inside a singleton, inject `IServiceProvider` and create a scope explicitly:

```csharp
public class OrderCache
{
    private readonly IServiceProvider _serviceProvider;

    public OrderCache(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task WarmCacheAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        // use repository within this explicitly managed scope
        var orders = await repository.GetRecentOrdersAsync();
        // populate cache...
    }
}
```

### Mistake 3: Over-Injection

If a class has seven or eight constructor parameters, that's usually a sign the class is doing too much. DI makes it *easy* to inject many things, but that doesn't mean you should.

```csharp
// This constructor is a code smell
public class OrderService(
    IOrderRepository repository,
    IInventoryService inventory,
    IPaymentGateway payments,
    IShippingCalculator shipping,
    INotificationService notifications,
    IFraudDetector fraud,
    IAuditLog audit,
    ILogger<OrderService> logger)
```

When you see this, consider whether you need the [Composition over Inheritance approach from early in the series](design-patterns-series-composition-over-complexity.html) and whether some of these concerns belong in their own service or pipeline step.

### Mistake 4: Registering Concrete Types When Interfaces Exist

```csharp
// Works, but couples callers to the concrete type
builder.Services.AddScoped<SqlOrderRepository>();

// Better — callers depend on the abstraction
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
```

Register by interface where possible. It keeps your options open for future swaps and makes testing easier.

### Best Practice: Register in the Composition Root

All DI registrations should happen in one place — `Program.cs`, or feature-grouped extension methods called from there. Don't scatter `ServiceCollection` calls across your domain classes.

```csharp
// Good — feature-grouped extension methods keep Program.cs clean
builder.Services.AddOrderingServices(builder.Configuration);
builder.Services.AddInventoryServices(builder.Configuration);
builder.Services.AddNotificationServices(builder.Configuration);
```

```csharp
// OrderingServices extension method
public static class OrderingServiceExtensions
{
    public static IServiceCollection AddOrderingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<SqlOrderRepository>();
        services.AddScoped<IOrderRepository>(sp => BuildDecoratedRepository(sp));
        services.AddScoped<IOrderPipelineHandler, OrderValidationHandler>();
        services.AddScoped<IOrderPipelineHandler, InventoryCheckHandler>();
        services.AddScoped<IOrderPipelineHandler, FraudDetectionHandler>();
        services.AddScoped<IOrderPipelineHandler, OrderPersistenceHandler>();
        services.AddScoped<OrderPipeline>();
        services.AddScoped<OrderService>();
        return services;
    }

    private static IOrderRepository BuildDecoratedRepository(IServiceProvider sp)
    {
        IOrderRepository inner = sp.GetRequiredService<SqlOrderRepository>();
        inner = new CachingOrderRepository(inner, sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<ILogger<CachingOrderRepository>>());
        inner = new LoggingOrderRepository(inner,
            sp.GetRequiredService<ILogger<LoggingOrderRepository>>());
        return inner;
    }
}
```

This keeps `Program.cs` readable and groups related registrations so they're easy to find.

### Best Practice: Validate at Startup

In development, enable scope validation and ensure all services can be constructed:

```csharp
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
```

`ValidateOnBuild` will throw at startup if any registered service has a dependency that isn't registered, or if there are captive dependency issues. This catches a whole class of runtime errors before your application ever serves a request.

## Wrapping Up the Series

Over the course of this series, we've built up a toolkit of patterns that work together to produce maintainable, scalable code. Let's look at what each one contributes:

**[Composition over Complexity](design-patterns-series-composition-over-complexity.html)** — the meta-principle. Build systems by composing small, focused pieces rather than building large, monolithic classes. Every pattern in this series is an application of this idea.

**[SOLID Principles](solid-principles-foundation-of-good-design.html)** — the foundation. Single Responsibility keeps classes focused. Open/Closed means you extend by adding, not modifying. Liskov Substitution makes interfaces substitutable. Interface Segregation keeps contracts small. Dependency Inversion points dependencies at abstractions. These aren't rules for their own sake — they're the properties that make composition work.

**[Strategy Pattern](strategy-pattern-swapping-algorithms-at-runtime.html)** — swap algorithms at runtime. Extract the varying behaviour behind an interface. Let the caller choose the implementation. Works beautifully with DI because you register each strategy and inject the right one.

**[Repository Pattern](repository-pattern-abstracting-data-access.html)** — abstract data access behind an interface. Your business logic doesn't know (or care) whether data comes from SQL Server, MongoDB, or a fake in-memory store. Makes testing trivial. Enables the Decorator.

**[Decorator Pattern](decorator-pattern-adding-behavior-without-modification.html)** — add cross-cutting behaviour without modifying existing classes. Wrap an interface with another implementation of the same interface. Stack them up. Add caching, logging, retry logic, and metrics without a single change to `SqlOrderRepository` or `OrderService`.

**[Factory Pattern](factory-pattern-hiding-object-creation.html)** — hide construction logic. Abstract the `new` calls. Combined with DI, it enables runtime selection of implementations based on request data or configuration.

**[Chain of Responsibility](chain-of-responsibility-passing-requests-through-handlers.html)** — build processing pipelines from independent handlers. Each handler does one thing. Any handler can short-circuit the chain. You configure the pipeline in the Composition Root — the handlers themselves don't know about each other.

**Dependency Injection** — this post — the pattern that assembles the others. It moves all `new` calls to the Composition Root. It makes dependencies explicit and substitutable. It enables testing by making seams visible. Without it, all the other patterns are harder to compose and harder to test.

Here's the key insight: **none of these patterns delivers its full value in isolation.** The Repository Pattern is more useful because DI makes it easy to swap implementations. The Decorator Pattern is elegant because DI lets you stack decorators with a single registration change. Chain of Responsibility scales to large pipelines because DI manages the handler instances and their lifetimes. The patterns amplify each other.

The codebase that uses all of these together looks like this:

- Business logic classes (`OrderService`, `OrderPipeline`) with small, focused constructors
- Implementations that each do one thing (`SqlOrderRepository`, `CachingOrderRepository`, `FraudDetectionHandler`)
- Interfaces everywhere, concretions at the edges
- A Composition Root that assembles the whole thing
- Unit tests that run in milliseconds against fake implementations

That's what maintainable, scalable code looks like in practice. Not any single pattern, but the combination of all of them, wired together cleanly with Dependency Injection.

I hope this series has given you a solid foundation to work from. These patterns aren't abstract theory — they're the same techniques you'll find in well-designed production .NET codebases every day. Start applying them one at a time and see what changes.

Thanks for following along.
