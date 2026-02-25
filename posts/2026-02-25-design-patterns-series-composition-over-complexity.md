---
title: "Design Patterns Series: Composition Over Complexity"
date: 2026-02-25
tags: csharp, design-patterns, architecture, best-practices
---

# Design Patterns Series: Composition Over Complexity

Welcome to the first article in a series about design patterns and principles in C#. Over the coming posts, I'll be exploring practical patterns that'll help you write cleaner, more maintainable code.

But before we dive into specific patterns, let's talk about a fundamental principle that underpins all of them: breaking down code into small, composable parts instead of writing giant procedures.

## The Series Roadmap

Here's what we'll be covering in this series:

1. **SOLID Principles** - The foundation of good object-oriented design
2. **Composition over Inheritance** - Building flexible systems with small parts
3. **Strategy Pattern** - Swapping algorithms at runtime
4. **Strangler Fig Pattern** - Gradually replacing legacy code
5. **Memoisation** - Caching expensive computations
6. **Factory Pattern** - Creating objects without exposing creation logic
7. **Repository Pattern** - Abstracting data access
8. **Decorator Pattern** - Adding behavior without modification
9. **Chain of Responsibility** - Passing requests through a chain of handlers
10. **Dependency Injection** - Loosely coupling your components

Each pattern addresses specific problems you'll encounter in real-world C# development. But they all share one thing: they break complex problems into smaller, manageable pieces.

## The Problem: Giant Methods

Let's start with something we've all seen (or written). Here's a typical "do everything" method:

```csharp
public void ProcessOrder(Order order)
{
    // Validate the order
    if (order == null) throw new ArgumentNullException(nameof(order));
    if (order.Items == null || !order.Items.Any()) 
        throw new InvalidOperationException("Order has no items");
    
    // Calculate totals
    decimal subtotal = 0;
    foreach (var item in order.Items)
    {
        subtotal += item.Price * item.Quantity;
    }
    
    decimal tax = subtotal * 0.08m;
    decimal shipping = subtotal > 100 ? 0 : 9.99m;
    decimal total = subtotal + tax + shipping;
    
    // Apply discounts
    if (order.Customer.IsPremium && subtotal > 50)
    {
        total = total * 0.9m; // 10% discount
    }
    
    // Process payment
    var paymentGateway = new PaymentGateway();
    var paymentResult = paymentGateway.Charge(order.Customer.CreditCard, total);
    
    if (!paymentResult.Success)
    {
        // Log the failure
        Console.WriteLine($"Payment failed: {paymentResult.ErrorMessage}");
        // Send email notification
        var emailService = new EmailService();
        emailService.Send(order.Customer.Email, "Payment Failed", paymentResult.ErrorMessage);
        throw new PaymentException("Payment processing failed");
    }
    
    // Update inventory
    foreach (var item in order.Items)
    {
        var product = _database.Products.Find(item.ProductId);
        product.StockQuantity -= item.Quantity;
        _database.SaveChanges();
    }
    
    // Send confirmation email
    var confirmationEmail = new EmailService();
    confirmationEmail.Send(
        order.Customer.Email, 
        "Order Confirmed", 
        $"Your order #{order.Id} for ${total} has been confirmed"
    );
}
```

What's wrong with this? It works, right? Well, yes. But it's also a nightmare to maintain, test, and extend.

## The Issues

This method violates several principles (we'll cover these in detail later in the series):

1. **Does too many things** - Validation, calculation, payment, inventory, email
2. **Hard to test** - You can't test validation without also testing payment processing
3. **Tight coupling** - Creates concrete dependencies (`new PaymentGateway()`, `new EmailService()`)
4. **Hard to change** - Want to change how discounts work? Better hope you don't break inventory updates
5. **Impossible to reuse** - Need just the calculation logic somewhere else? Copy-paste time

## The Solution: Break It Down

Let's refactor this by breaking it into small, focused pieces:

```csharp
public class OrderProcessor
{
    private readonly IOrderValidator _validator;
    private readonly IOrderCalculator _calculator;
    private readonly IPaymentService _paymentService;
    private readonly IInventoryService _inventoryService;
    private readonly INotificationService _notificationService;
    
    public OrderProcessor(
        IOrderValidator validator,
        IOrderCalculator calculator,
        IPaymentService paymentService,
        IInventoryService inventoryService,
        INotificationService notificationService)
    {
        _validator = validator;
        _calculator = calculator;
        _paymentService = paymentService;
        _inventoryService = inventoryService;
        _notificationService = notificationService;
    }
    
    public void ProcessOrder(Order order)
    {
        _validator.Validate(order);
        
        var total = _calculator.CalculateTotal(order);
        
        var paymentResult = _paymentService.ProcessPayment(order.Customer, total);
        
        if (!paymentResult.Success)
        {
            _notificationService.NotifyPaymentFailure(order.Customer, paymentResult);
            throw new PaymentException("Payment processing failed");
        }
        
        _inventoryService.UpdateInventory(order.Items);
        
        _notificationService.SendOrderConfirmation(order, total);
    }
}
```

Look at that! Our `ProcessOrder` method is now just six lines that clearly show the flow. Each responsibility is delegated to a specialized component.

## What We Gained

By breaking this down into smaller pieces, we get:

1. **Testability** - Test each component in isolation
2. **Flexibility** - Swap out implementations (different payment gateways, notification methods)
3. **Clarity** - The high-level flow is immediately obvious
4. **Reusability** - Need order calculation elsewhere? Just inject `IOrderCalculator`
5. **Maintainability** - Changes to tax logic don't risk breaking email notifications

This is composition in action. We've composed a complex operation from simple, focused parts.

## The Core Principle

Throughout this series, you'll notice a pattern (pun intended). Whether we're talking about SOLID, Strategy, or Strangler Fig, they all encourage:

- **Small, focused components** that do one thing well
- **Clear interfaces** that define contracts
- **Loose coupling** between components
- **High cohesion** within components

When you break code into small parts, you're not just making it easier to understand. You're making it easier to test, easier to change, and easier to extend. That's the real power of design patterns.

## What's Next

In the next post, we'll dive into the SOLID principles, starting with the Single Responsibility Principle (which we just demonstrated here). We'll explore why these five principles form the foundation of good object-oriented design and how to apply them in C#.

Until then, take a look at your codebase. Find that one method that does everything. You know the one I'm talking about. Now imagine breaking it into small, composable pieces. How would that change your tests? Your deployment strategy? Your team's ability to work in parallel?

That's the power of composition over complexity.
