---
title: Strangler Fig Pattern - Gradually Replacing Legacy Code
date: 2026-03-01
tags: csharp, design-patterns, strangler-fig, legacy-code, architecture
image: strangler-fig-pattern-gradually-replacing-legacy-code.png
---

We've been working through design patterns that make your code more flexible. We covered [composition over inheritance](composition-over-inheritance-building-flexible-systems.html), the [Strategy Pattern](strategy-pattern-swapping-algorithms-at-runtime.html), and the [SOLID principles](solid-principles-foundation-of-good-design.html) that underpin them all. But what do you do when you're staring at a massive legacy system that needs to be replaced?

You strangle it. Slowly.

The Strangler Fig Pattern (named after trees that grow around and eventually replace their hosts) is a strategy for incrementally replacing legacy systems. Instead of a risky big-bang rewrite, you gradually replace functionality piece by piece while keeping the old system running. Let's see how this works in practice.

## The Problem: Big Bang Rewrites Are Dangerous

You inherit a monolithic order processing system built ten years ago. It's tightly coupled, hard to test, and missing modern features. Your instinct? "Let's rewrite the whole thing!"

Here's why that's risky:

1. **Business keeps moving** - Requirements change while you're rewriting
2. **Unknown unknowns** - The old system has hidden business logic you'll miss
3. **No incremental value** - You ship nothing until the entire rewrite is done
4. **Risk everything** - If the rewrite fails, you've wasted months (or years)
5. **Parallel maintenance** - Teams have to maintain the old system *and* build the new one

The Strangler Fig Pattern offers a safer alternative: replace the system one piece at a time.

## The Pattern: Incremental Replacement

The core idea is simple:

1. **Identify a boundary** - Pick a piece of functionality to replace (e.g., payment processing)
2. **Build the replacement** - Create the new implementation alongside the old system
3. **Route traffic** - Redirect requests to the new implementation
4. **Monitor and verify** - Ensure the new code works correctly
5. **Remove the old code** - Delete the legacy implementation
6. **Repeat** - Pick the next piece and start again

Let's see this in action.

## Starting Simple: Replacing a Notification System

Your legacy system sends emails directly using SMTP. You want to move to a modern service like SendGrid, but you can't risk breaking notifications for thousands of users.

Here's the legacy code (simplified):

```csharp
public class OrderService
{
    public void ProcessOrder(Order order)
    {
        // ... order processing logic ...
        
        // Legacy email sending
        var smtpClient = new SmtpClient("mail.oldserver.com");
        smtpClient.Send(
            "orders@company.com",
            order.Customer.Email,
            "Order Confirmed",
            $"Your order #{order.Id} is confirmed"
        );
    }
}
```

### Step 1: Create an Abstraction

First, extract an interface that represents the notification concept:

```csharp
public interface INotificationService
{
    Task SendOrderConfirmationAsync(Order order);
}
```

### Step 2: Wrap the Legacy Code

Create an adapter that implements the interface using the old code:

```csharp
public class LegacySmtpNotificationService : INotificationService
{
    public Task SendOrderConfirmationAsync(Order order)
    {
        // This is the old SMTP code, unchanged
        var smtpClient = new SmtpClient("mail.oldserver.com");
        smtpClient.Send(
            "orders@company.com",
            order.Customer.Email,
            "Order Confirmed",
            $"Your order #{order.Id} is confirmed"
        );
        
        return Task.CompletedTask;
    }
}
```

### Step 3: Build the New Implementation

Now create the replacement using SendGrid:

```csharp
public class SendGridNotificationService : INotificationService
{
    private readonly SendGridClient _client;
    
    public SendGridNotificationService(SendGridClient client)
    {
        _client = client;
    }
    
    public async Task SendOrderConfirmationAsync(Order order)
    {
        var message = new SendGridMessage
        {
            From = new EmailAddress("orders@company.com"),
            Subject = "Order Confirmed",
            PlainTextContent = $"Your order #{order.Id} is confirmed"
        };
        
        message.AddTo(new EmailAddress(order.Customer.Email));
        
        await _client.SendEmailAsync(message);
    }
}
```

### Step 4: Route Traffic with a Facade

Here's the clever part. Create a facade that can route to either implementation:

```csharp
public class NotificationServiceFacade : INotificationService
{
    private readonly INotificationService _legacy;
    private readonly INotificationService _new;
    private readonly IFeatureToggle _featureToggle;
    
    public NotificationServiceFacade(
        LegacySmtpNotificationService legacy,
        SendGridNotificationService newService,
        IFeatureToggle featureToggle)
    {
        _legacy = legacy;
        _new = newService;
        _featureToggle = featureToggle;
    }
    
    public async Task SendOrderConfirmationAsync(Order order)
    {
        if (_featureToggle.IsEnabled("UseSendGrid"))
        {
            await _new.SendOrderConfirmationAsync(order);
        }
        else
        {
            await _legacy.SendOrderConfirmationAsync(order);
        }
    }
}
```

### Step 5: Gradually Roll Out

Now you can control which users get the new implementation:

```csharp
public class FeatureToggleService : IFeatureToggle
{
    public bool IsEnabled(string feature)
    {
        if (feature == "UseSendGrid")
        {
            // Start with 5% of traffic
            return Random.Shared.Next(100) < 5;
        }
        
        return false;
    }
}
```

Your order service now uses the facade:

```csharp
public class OrderService
{
    private readonly INotificationService _notifications;
    
    public OrderService(INotificationService notifications)
    {
        _notifications = notifications;
    }
    
    public async Task ProcessOrderAsync(Order order)
    {
        // ... order processing logic ...
        
        await _notifications.SendOrderConfirmationAsync(order);
    }
}
```

You've just strangled the first piece of legacy code! The old SMTP system is still there, but now you can gradually increase the percentage until you're 100% on SendGrid. Once you're confident, you delete the legacy adapter.

## Real-World Example: Replacing a Monolithic API

Let's scale this up. You have a massive ASP.NET Web API that handles everything: orders, inventory, customers, payments. It's one giant project with shared state and tight coupling. You want to move to microservices.

### Step 1: Identify Service Boundaries

Break down the monolith conceptually:

- **Orders Service** - Handles order creation, updates, status
- **Inventory Service** - Tracks stock levels
- **Customer Service** - Manages customer data
- **Payment Service** - Processes payments

### Step 2: Create a Routing Layer

Build an API gateway that sits in front of both systems:

```csharp
public class OrdersController : ControllerBase
{
    private readonly IFeatureToggle _featureToggle;
    private readonly ILegacyOrdersClient _legacyClient;
    private readonly IOrdersService _newService;
    
    public OrdersController(
        IFeatureToggle featureToggle,
        ILegacyOrdersClient legacyClient,
        IOrdersService newService)
    {
        _featureToggle = featureToggle;
        _legacyClient = legacyClient;
        _newService = newService;
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        if (_featureToggle.IsEnabled("UseNewOrdersService"))
        {
            var order = await _newService.CreateOrderAsync(request);
            return Ok(order);
        }
        else
        {
            var order = await _legacyClient.CreateOrderAsync(request);
            return Ok(order);
        }
    }
}
```

### Step 3: Parallel Run for Safety

During migration, you can run both systems and compare results:

```csharp
[HttpPost]
public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
{
    // Always run the legacy system (it's our source of truth for now)
    var legacyOrder = await _legacyClient.CreateOrderAsync(request);
    
    if (_featureToggle.IsEnabled("ParallelRunNewOrdersService"))
    {
        try
        {
            // Also run the new system
            var newOrder = await _newService.CreateOrderAsync(request);
            
            // Compare results
            if (!OrdersMatch(legacyOrder, newOrder))
            {
                _logger.LogWarning(
                    "Orders service mismatch for request {RequestId}", 
                    request.Id
                );
            }
        }
        catch (Exception ex)
        {
            // Don't fail the request if the new system has issues
            _logger.LogError(ex, "New orders service failed");
        }
    }
    
    // Return the legacy result
    return Ok(legacyOrder);
}
```

This lets you verify the new system is working correctly *before* you switch traffic to it.

### Step 4: Incremental Cutover

Once you're confident, start routing real traffic:

```csharp
public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
{
    var useNewService = _featureToggle.IsEnabled("UseNewOrdersService");
    
    if (useNewService)
    {
        try
        {
            var newOrder = await _newService.CreateOrderAsync(request);
            return Ok(newOrder);
        }
        catch (Exception ex)
        {
            // Fallback to legacy on error
            _logger.LogError(ex, "New service failed, falling back to legacy");
            var legacyOrder = await _legacyClient.CreateOrderAsync(request);
            return Ok(legacyOrder);
        }
    }
    else
    {
        var legacyOrder = await _legacyClient.CreateOrderAsync(request);
        return Ok(legacyOrder);
    }
}
```

Start with 1% of traffic, then 5%, then 25%, then 100%. If anything breaks, you can instantly roll back by toggling the feature flag.

## Advanced: Data Migration

The hardest part of strangling legacy systems is usually the data. Here's a pattern that works:

### Dual Writes During Transition

When you're replacing a service, write to both old and new databases:

```csharp
public class OrderService : IOrderService
{
    private readonly ILegacyOrderRepository _legacyRepo;
    private readonly INewOrderRepository _newRepo;
    private readonly IFeatureToggle _toggle;
    
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        Order order;
        
        if (_toggle.IsEnabled("UseNewOrdersDatabase"))
        {
            // New database is the source of truth
            order = await _newRepo.CreateAsync(request);
            
            // Also write to legacy for safety
            if (_toggle.IsEnabled("DualWriteToLegacy"))
            {
                await _legacyRepo.CreateAsync(order);
            }
        }
        else
        {
            // Legacy database is the source of truth
            order = await _legacyRepo.CreateAsync(request);
            
            // Also write to new database
            await _newRepo.CreateAsync(order);
        }
        
        return order;
    }
}
```

This ensures both databases stay in sync during the transition.

### Background Migration

For existing data, run a background job that copies records from old to new:

```csharp
public class OrderMigrationJob
{
    private readonly ILegacyOrderRepository _legacyRepo;
    private readonly INewOrderRepository _newRepo;
    
    public async Task MigrateOrdersAsync(CancellationToken cancellationToken)
    {
        var batchSize = 1000;
        var lastMigratedId = await GetLastMigratedIdAsync();
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var orders = await _legacyRepo.GetBatchAsync(
                lastMigratedId, 
                batchSize
            );
            
            if (!orders.Any())
                break;
            
            foreach (var order in orders)
            {
                await _newRepo.CreateAsync(order);
                lastMigratedId = order.Id;
            }
            
            await SaveLastMigratedIdAsync(lastMigratedId);
        }
    }
}
```

This lets you migrate data gradually without blocking new features.

## When to Use the Strangler Fig Pattern

Use this pattern when:

1. **You have a large legacy system** that's too risky to replace all at once
2. **The business can't pause** for a months-long rewrite
3. **You need to deliver value incrementally** rather than all at the end
4. **You're unsure of all the requirements** in the legacy system
5. **You want to minimize risk** by being able to roll back at any time

Don't use it when:

1. **The system is small enough** to rewrite in a few weeks
2. **There's no clear way to partition** the functionality
3. **The legacy system is stable** and doesn't need replacement
4. **You have complete requirements** and high confidence in a rewrite

## Tips for Success

**Start with low-risk features**: Don't begin with critical payment processing. Start with something like notification delivery or reporting.

**Use feature flags extensively**: Tools like LaunchDarkly or Azure App Configuration make it easy to toggle between implementations.

**Monitor everything**: Track error rates, performance, and business metrics for both old and new systems.

**Have a rollback plan**: Always be able to switch back to the legacy system instantly.

**Delete aggressively**: Once a piece is fully migrated and the legacy code isn't needed, delete it. Don't let it linger.

**Celebrate small wins**: Each successful migration is progress. Share it with the team.

## Wrapping Up

The Strangler Fig Pattern isn't glamorous. There's no dramatic "we shipped the new system!" moment. Instead, you gradually, methodically replace functionality until one day you realize the legacy system is gone.

But that's exactly why it works. By avoiding the big-bang rewrite, you reduce risk, deliver value continuously, and learn as you go. You're not betting the company on a single deployment. You're making small, reversible decisions that compound over time.

This pattern builds on everything we've covered in this series: [SOLID principles](solid-principles-foundation-of-good-design.html) (especially Interface Segregation and Dependency Inversion), [composition](composition-over-inheritance-building-flexible-systems.html) (building from small parts), and the [Strategy Pattern](strategy-pattern-swapping-algorithms-at-runtime.html) (swapping implementations).

Next time someone suggests a big rewrite, ask: "Could we strangle this instead?" Extract an interface. Build a facade. Route some traffic. Monitor. Migrate. Delete. Repeat. That's how you safely replace legacy systems without betting everything on one roll of the dice.

The strangler fig grows slowly, wrapping around its host tree. Eventually, the host is gone and the fig stands alone. Your legacy system can go the same way – one feature at a time.
