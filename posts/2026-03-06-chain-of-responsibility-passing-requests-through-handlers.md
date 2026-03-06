---
title: Chain of Responsibility - Passing Requests Through Handlers
date: 2026-03-06
tags: csharp, design-patterns, chain-of-responsibility, architecture, middleware
image: chain-of-responsibility-passing-requests-through-handlers.png
---

At the end of the [Decorator Pattern post](decorator-pattern-adding-behavior-without-modification.html) I mentioned that Chain of Responsibility passes a request through a sequence of handlers, each deciding whether to handle it, modify it, or pass it on. If you've used ASP.NET Core middleware, you've already used this pattern — you just might not have known its name.

The Decorator Pattern stacks wrappers around a single object. Chain of Responsibility is different: each handler in the chain decides what to do with the request and whether to pass it along. A handler can stop the chain entirely, short-circuit with an early response, or transform the request before passing it downstream. The sender doesn't know which handler will ultimately process the request. That's the key decoupling this pattern provides.

## The Problem: Nested Conditionals

Here's a typical order validation method without Chain of Responsibility:

```csharp
public class OrderService
{
    public Result PlaceOrder(Order order)
    {
        if (order == null)
            return Result.Failure("Order cannot be null");

        if (string.IsNullOrEmpty(order.CustomerId))
            return Result.Failure("Customer ID is required");

        if (!order.Items.Any())
            return Result.Failure("Order must contain at least one item");

        if (order.Items.Any(i => i.Quantity <= 0))
            return Result.Failure("All items must have a positive quantity");

        if (order.Items.Any(i => i.UnitPrice <= 0))
            return Result.Failure("All items must have a valid price");

        if (order.Total > 10_000m)
            return Result.Failure("Order exceeds maximum value limit");

        // Finally, place the order
        return ProcessOrder(order);
    }
}
```

This works, but it doesn't scale. As the business grows, you'll add more validation rules. Some rules apply only to certain customers. Some rules change based on a feature flag. Some rules need to be skipped in test environments. Before long you're looking at a method with dozens of conditions, nested `if` blocks, and flags passed in from outside.

More importantly, none of these rules can be tested in isolation. Testing "items must have positive quantities" means constructing a valid order, filling in all the required fields, and then checking that specific failure. Testing one rule drags all the others in.

## The Handler Interface

The Chain of Responsibility pattern solves this by turning each check into its own class. Each handler knows about the next handler in the chain, and each one decides: handle it myself, pass it along, or stop the chain.

Here's the core abstraction:

```csharp
public abstract class OrderHandler
{
    private OrderHandler? _next;

    public OrderHandler SetNext(OrderHandler next)
    {
        _next = next;
        return next; // return next to allow fluent chaining
    }

    public abstract Result Handle(Order order);

    protected Result PassToNext(Order order)
    {
        if (_next is null)
            return Result.Success();

        return _next.Handle(order);
    }
}
```

Each concrete handler extends this and either handles the request or calls `PassToNext`:

```csharp
public class NullOrderHandler : OrderHandler
{
    public override Result Handle(Order order)
    {
        if (order == null)
            return Result.Failure("Order cannot be null");

        return PassToNext(order);
    }
}

public class CustomerIdHandler : OrderHandler
{
    public override Result Handle(Order order)
    {
        if (string.IsNullOrEmpty(order.CustomerId))
            return Result.Failure("Customer ID is required");

        return PassToNext(order);
    }
}

public class OrderItemsHandler : OrderHandler
{
    public override Result Handle(Order order)
    {
        if (!order.Items.Any())
            return Result.Failure("Order must contain at least one item");

        if (order.Items.Any(i => i.Quantity <= 0))
            return Result.Failure("All items must have a positive quantity");

        if (order.Items.Any(i => i.UnitPrice <= 0))
            return Result.Failure("All items must have a valid price");

        return PassToNext(order);
    }
}

public class OrderValueHandler : OrderHandler
{
    private readonly decimal _maxOrderValue;

    public OrderValueHandler(decimal maxOrderValue)
    {
        _maxOrderValue = maxOrderValue;
    }

    public override Result Handle(Order order)
    {
        if (order.Total > _maxOrderValue)
            return Result.Failure($"Order exceeds maximum value of {_maxOrderValue:C}");

        return PassToNext(order);
    }
}
```

You wire them together in a chain:

```csharp
var nullCheck = new NullOrderHandler();
var customerCheck = new CustomerIdHandler();
var itemsCheck = new OrderItemsHandler();
var valueCheck = new OrderValueHandler(maxOrderValue: 10_000m);

nullCheck
    .SetNext(customerCheck)
    .SetNext(itemsCheck)
    .SetNext(valueCheck);

var result = nullCheck.Handle(order);
```

Each handler is now independently testable. The `OrderValueHandler` test doesn't need a fully populated order — it just needs an order with a total, and a handler instance with a max value. The chain is assembled separately from the handlers themselves.

## Using an Interface Instead

The abstract base class works, but an interface gives you more flexibility. You might want to use a lambda, a mock, or a handler that doesn't fit the inheritance hierarchy:

```csharp
public interface IOrderHandler
{
    Result Handle(Order order);
}
```

A simple linked-list implementation:

```csharp
public class OrderHandlerChain : IOrderHandler
{
    private readonly IReadOnlyList<IOrderHandler> _handlers;

    public OrderHandlerChain(IReadOnlyList<IOrderHandler> handlers)
    {
        _handlers = handlers;
    }

    public Result Handle(Order order)
    {
        foreach (var handler in _handlers)
        {
            var result = handler.Handle(order);
            if (!result.IsSuccess)
                return result;
        }

        return Result.Success();
    }
}
```

Now the chain is just a list. You compose it with the handlers you need:

```csharp
var chain = new OrderHandlerChain(new List<IOrderHandler>
{
    new NullOrderHandler(),
    new CustomerIdHandler(),
    new OrderItemsHandler(),
    new OrderValueHandler(maxOrderValue: 10_000m)
});

var result = chain.Handle(order);
```

This approach is simpler to understand and easier to test: `OrderHandlerChain` is tested separately from the handlers, and handlers don't need a `SetNext` method at all.

## A Real-World Validation Pipeline

Here's a more complete example where handlers share context through a pipeline object. This pattern is useful when handlers need to communicate — one handler might enrich the request, and downstream handlers use that enriched data.

```csharp
public class OrderContext
{
    public Order Order { get; init; } = null!;
    public Customer? Customer { get; set; }
    public List<string> Warnings { get; } = new();
}

public interface IOrderPipelineHandler
{
    Result Handle(OrderContext context);
}
```

Handlers can now enrich the context and pass it along:

```csharp
public class CustomerLookupHandler : IOrderPipelineHandler
{
    private readonly ICustomerRepository _customers;

    public CustomerLookupHandler(ICustomerRepository customers)
    {
        _customers = customers;
    }

    public Result Handle(OrderContext context)
    {
        var customer = _customers.GetById(context.Order.CustomerId);

        if (customer is null)
            return Result.Failure($"Customer {context.Order.CustomerId} not found");

        context.Customer = customer; // enrich for downstream handlers
        return Result.Success();
    }
}

public class CreditLimitHandler : IOrderPipelineHandler
{
    public Result Handle(OrderContext context)
    {
        // uses the Customer loaded by CustomerLookupHandler
        if (context.Customer is null)
            return Result.Failure("Customer not loaded");

        if (context.Order.Total > context.Customer.CreditLimit)
            return Result.Failure(
                $"Order total {context.Order.Total:C} exceeds credit limit {context.Customer.CreditLimit:C}");

        return Result.Success();
    }
}

public class FraudCheckHandler : IOrderPipelineHandler
{
    private readonly IFraudService _fraudService;

    public FraudCheckHandler(IFraudService fraudService)
    {
        _fraudService = fraudService;
    }

    public Result Handle(OrderContext context)
    {
        if (_fraudService.IsSuspicious(context.Order))
        {
            context.Warnings.Add("Order flagged for review");
            // Warning only — chain continues
        }

        return Result.Success();
    }
}
```

The pipeline runs all handlers in order. If any handler fails, the chain stops:

```csharp
public class OrderPipeline
{
    private readonly IReadOnlyList<IOrderPipelineHandler> _handlers;

    public OrderPipeline(IReadOnlyList<IOrderPipelineHandler> handlers)
    {
        _handlers = handlers;
    }

    public Result Execute(Order order)
    {
        var context = new OrderContext { Order = order };

        foreach (var handler in _handlers)
        {
            var result = handler.Handle(context);
            if (!result.IsSuccess)
                return result;
        }

        return Result.Success();
    }
}
```

Notice that `FraudCheckHandler` adds a warning but doesn't fail the pipeline. The chain continues. That's intentional: some handlers handle a request fully, some transform it, some only run side effects. This is the flexibility that Chain of Responsibility gives you.

## ASP.NET Core Middleware: Chain of Responsibility in the Framework

If you've written ASP.NET Core middleware, you've been using Chain of Responsibility without necessarily naming it. The middleware pipeline is exactly this pattern:

```csharp
app.Use(async (context, next) =>
{
    // Before the rest of the pipeline
    Console.WriteLine($"Request: {context.Request.Path}");

    await next(context); // call the next handler

    // After the rest of the pipeline
    Console.WriteLine($"Response: {context.Response.StatusCode}");
});

app.UseAuthentication(); // another handler
app.UseAuthorization();  // another handler

app.MapControllers();    // terminal handler — doesn't call next
```

Each `Use` call adds a handler to the chain. Each handler receives the `HttpContext` (the "request") and a `next` delegate (the reference to the next handler). A handler can:

- **Pass through** — call `next`, do nothing else
- **Short-circuit** — write a response without calling `next` (the way `UseAuthentication` returns 401 for unauthenticated requests)
- **Transform** — modify the request or response before or after calling `next`
- **Handle completely** — `MapControllers` is the terminal handler that processes the request and returns a response

This is exactly the pattern we built above, just expressed as lambdas and delegates rather than classes. The design is the same.

## Composing the Chain at Registration Time

Because handlers are separate classes, you can compose the chain differently depending on context. In development you might add verbose logging handlers. In tests you might add a mock fraud check. In production you get the real thing.

With dependency injection:

```csharp
// In your DI setup
services.AddScoped<IOrderPipelineHandler, CustomerLookupHandler>();
services.AddScoped<IOrderPipelineHandler, CreditLimitHandler>();
services.AddScoped<IOrderPipelineHandler, FraudCheckHandler>();
services.AddScoped<OrderPipeline>();

// OrderPipeline receives all IOrderPipelineHandler instances
public class OrderPipeline
{
    private readonly IReadOnlyList<IOrderPipelineHandler> _handlers;

    public OrderPipeline(IEnumerable<IOrderPipelineHandler> handlers)
    {
        _handlers = handlers.ToList().AsReadOnly();
    }
}
```

ASP.NET Core's DI container will inject all registered `IOrderPipelineHandler` implementations in registration order. Adding a new handler to the pipeline is a single line in your DI configuration — the `OrderPipeline` class and the `OrderService` don't change at all.

That's the key benefit: the sender (`OrderService`) is completely decoupled from the receivers. It doesn't know how many handlers there are, which ones exist, or in what order they run. It just passes the request to the pipeline and waits for a result.

## Comparing to the Decorator Pattern

At first glance, Chain of Responsibility and Decorator look similar. Both chain objects together. Both let you add behaviour without modifying existing classes. What's different?

**Decorator** wraps a single object. Every decorator in the chain processes every call. The chain always reaches the innermost object. There's no conditional short-circuiting — the behaviour is additive.

**Chain of Responsibility** passes a request through a sequence of potential handlers. Any handler can stop the chain early. The handlers aren't wrapping each other — they're passing a request along. The request might not reach the end of the chain at all.

In the [Decorator Pattern](decorator-pattern-adding-behavior-without-modification.html), the caching decorator always calls the inner repository after checking the cache. The chain always goes all the way down. Chain of Responsibility is different — the authentication middleware short-circuits the entire pipeline for unauthenticated requests. The controllers never see the request.

Use Decorator when you want additive behaviour that always fires. Use Chain of Responsibility when any handler might stop the chain.

## When to Use Chain of Responsibility

Use it when:

1. **Multiple objects might handle a request, and you don't know which one at compile time** — The handler is determined at runtime based on request properties.
2. **You want to issue a request to one of several objects without specifying the receiver explicitly** — The classic case: a support ticket escalation system where Level 1 handles simple issues, Level 2 handles complex ones, and Level 3 handles escalations.
3. **You have a series of checks or transformations that need to be independently configurable** — Validation pipelines, middleware chains, approval workflows.
4. **You want to be able to reorder, add, or remove handlers without modifying existing code** — The chain composition is separate from the handler implementations.

Skip it when:

1. **You always need every step to run** — If all handlers must always fire (like the Decorator Pattern), Chain of Responsibility adds complexity without benefit.
2. **The chain is trivially short and static** — Two conditions in one method are easier to read than two handler classes and a chain setup.
3. **Handler order matters and is complex** — If the ordering logic itself becomes complicated, consider whether a different design (like an explicit state machine) would be clearer.

## Wrapping Up

Chain of Responsibility lets you build processing pipelines from independent, reusable handlers. The sender of a request doesn't know who handles it. Handlers don't know about each other except for the next link. You can add, remove, and reorder handlers by changing the chain composition — without touching the handlers themselves or the code that triggers the chain.

You've seen this pattern at work in ASP.NET Core middleware, and now you can apply it to your own validation pipelines, approval workflows, and anywhere you need a series of conditional checks to stay flexible and independently testable.

We've now seen three patterns that work at different levels of the same problem:

- The [Repository Pattern](repository-pattern-abstracting-data-access.html) abstracts *where* data comes from
- The [Decorator Pattern](decorator-pattern-adding-behavior-without-modification.html) adds *cross-cutting behaviour* without modifying the original
- Chain of Responsibility handles *conditional processing pipelines* where any step might terminate early

Up next: **Dependency Injection** — the pattern that ties all of these together. Every pattern in this series has depended on injecting interfaces rather than creating concretions directly. In the next post, we'll look at why that matters, how .NET's built-in DI container works, and how to wire up all these patterns cleanly in a real application.
