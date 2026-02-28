---
title: Strategy Pattern - Swapping Algorithms at Runtime
date: 2026-02-28
tags: csharp, design-patterns, strategy-pattern, architecture
image: strategy-pattern-swapping-algorithms-at-runtime.png
---

We've been exploring design patterns that make your code more flexible and maintainable. We talked about [composition over inheritance](composition-over-inheritance-building-flexible-systems.html) yesterday, where you build systems by combining behaviors rather than creating complex hierarchies. Today's pattern – the Strategy Pattern – is the *practical application* of that principle.

Here's the core idea: instead of hard-coding an algorithm into your class, you define it as an interface and swap implementations at runtime. It's that simple. Let's see why you'd want to do this.

## The Problem: Hard-Coded Algorithms

You're building an e-commerce system, and you need to calculate shipping costs. Your first pass looks like this:

```csharp
public class Order
{
    public decimal Total { get; set; }
    public string ShippingMethod { get; set; }
    
    public decimal CalculateShipping()
    {
        if (ShippingMethod == "Standard")
        {
            return 5.99m;
        }
        else if (ShippingMethod == "Express")
        {
            return 12.99m;
        }
        else if (ShippingMethod == "Overnight")
        {
            return 24.99m;
        }
        else if (ShippingMethod == "International")
        {
            return Total * 0.15m; // 15% of order total
        }
        
        return 0m;
    }
}
```

This works, but it violates the Open/Closed Principle we covered in the [SOLID post](solid-principles-foundation-of-good-design.html). Every time you add a new shipping method, you're modifying this method. The if-else chain grows. Testing becomes harder because you need to test every branch. And God help you if you need the same logic somewhere else – you'll be copy-pasting this mess.

## Enter the Strategy Pattern

The Strategy Pattern says: extract each algorithm into its own class. Here's how it looks:

```csharp
// The strategy interface
public interface IShippingStrategy
{
    decimal CalculateCost(Order order);
}

// Concrete strategies
public class StandardShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 5.99m;
}

public class ExpressShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 12.99m;
}

public class OvernightShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 24.99m;
}

public class InternationalShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => order.Total * 0.15m;
}
```

Now your `Order` class just delegates to the strategy:

```csharp
public class Order
{
    public decimal Total { get; set; }
    private IShippingStrategy _shippingStrategy;
    
    public Order(IShippingStrategy shippingStrategy)
    {
        _shippingStrategy = shippingStrategy;
    }
    
    public void SetShippingStrategy(IShippingStrategy strategy)
    {
        _shippingStrategy = strategy;
    }
    
    public decimal CalculateShipping()
    {
        return _shippingStrategy.CalculateCost(this);
    }
}
```

Look at what we've gained:

1. **Easy to extend**: New shipping method? Create a new class. No touching existing code.
2. **Easy to test**: Each strategy is a single class with a single responsibility. Write one test per strategy.
3. **Easy to reuse**: Need shipping calculations in your invoice system? Use the same strategies.
4. **Runtime flexibility**: Change strategies on the fly based on user input or business rules.

## Swapping at Runtime

Here's where it gets interesting. You can change the algorithm while the program's running:

```csharp
var order = new Order(new StandardShipping())
{
    Total = 100.00m
};

Console.WriteLine($"Standard: ${order.CalculateShipping()}"); // $5.99

// Customer upgrades to express
order.SetShippingStrategy(new ExpressShipping());
Console.WriteLine($"Express: ${order.CalculateShipping()}"); // $12.99

// Later, they add an international item
order.SetShippingStrategy(new InternationalShipping());
Console.WriteLine($"International: ${order.CalculateShipping()}"); // $15.00
```

That's the power of the Strategy Pattern. The `Order` doesn't know *how* shipping is calculated – it just knows *that* it can be calculated.

## Real-World Example: Payment Processing

Let's take it up a notch with a more complex example. You're building a payment system that needs to handle multiple payment processors (Stripe, PayPal, Authorize.Net):

```csharp
public interface IPaymentStrategy
{
    Task<PaymentResult> ProcessPaymentAsync(decimal amount, string currency);
}

public class StripePaymentStrategy : IPaymentStrategy
{
    private readonly StripeClient _client;
    
    public StripePaymentStrategy(StripeClient client)
    {
        _client = client;
    }
    
    public async Task<PaymentResult> ProcessPaymentAsync(decimal amount, string currency)
    {
        // Stripe-specific logic
        var options = new ChargeCreateOptions
        {
            Amount = (long)(amount * 100), // Stripe uses cents
            Currency = currency.ToLower()
        };
        
        var charge = await _client.Charges.CreateAsync(options);
        
        return new PaymentResult
        {
            Success = charge.Status == "succeeded",
            TransactionId = charge.Id
        };
    }
}

public class PayPalPaymentStrategy : IPaymentStrategy
{
    private readonly PayPalHttpClient _client;
    
    public PayPalPaymentStrategy(PayPalHttpClient client)
    {
        _client = client;
    }
    
    public async Task<PaymentResult> ProcessPaymentAsync(decimal amount, string currency)
    {
        // PayPal-specific logic
        var request = new OrdersCreateRequest();
        request.RequestBody(new OrderRequest
        {
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    AmountWithBreakdown = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2")
                    }
                }
            }
        });
        
        var response = await _client.Execute(request);
        var order = response.Result<Order>();
        
        return new PaymentResult
        {
            Success = order.Status == "APPROVED",
            TransactionId = order.Id
        };
    }
}
```

Your checkout process doesn't care which processor you're using:

```csharp
public class CheckoutService
{
    private readonly IPaymentStrategy _paymentStrategy;
    
    public CheckoutService(IPaymentStrategy paymentStrategy)
    {
        _paymentStrategy = paymentStrategy;
    }
    
    public async Task<bool> CompleteCheckout(ShoppingCart cart)
    {
        var result = await _paymentStrategy.ProcessPaymentAsync(
            cart.Total, 
            cart.Currency
        );
        
        if (result.Success)
        {
            await SaveOrderAsync(cart, result.TransactionId);
            await SendConfirmationEmailAsync(cart);
        }
        
        return result.Success;
    }
}
```

## Choosing Strategies Dynamically

Here's where the Strategy Pattern really shines. You can select strategies based on runtime conditions:

```csharp
public class PaymentStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;
    
    public PaymentStrategyFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public IPaymentStrategy GetStrategy(string processor)
    {
        return processor.ToLower() switch
        {
            "stripe" => _serviceProvider.GetRequiredService<StripePaymentStrategy>(),
            "paypal" => _serviceProvider.GetRequiredService<PayPalPaymentStrategy>(),
            "authorizenet" => _serviceProvider.GetRequiredService<AuthorizeNetPaymentStrategy>(),
            _ => throw new ArgumentException($"Unknown payment processor: {processor}")
        };
    }
}
```

Or pick based on business rules:

```csharp
public IShippingStrategy GetShippingStrategy(Order order)
{
    // Business rule: free shipping over $100
    if (order.Total > 100m)
        return new FreeShipping();
    
    // Business rule: international addresses use special pricing
    if (order.Address.Country != "US")
        return new InternationalShipping();
    
    // Default to standard
    return new StandardShipping();
}
```

## Strategy Pattern in .NET

You're probably already using the Strategy Pattern without realizing it. Here are some examples from the .NET framework:

**IComparer<T>** – Different sorting strategies:

```csharp
var people = new List<Person>();

// Sort by age
people.Sort(new AgeComparer());

// Sort by name
people.Sort(new NameComparer());

// Sort with custom lambda (also a strategy!)
people.Sort((a, b) => a.LastName.CompareTo(b.LastName));
```

**Stream processing** – Different compression strategies:

```csharp
using var file = File.Create("data.bin");

// No compression strategy
using var stream = file;

// GZip compression strategy
using var compressed = new GZipStream(file, CompressionMode.Compress);

// Both use the same Stream interface
await stream.WriteAsync(data);
```

**Authentication handlers in ASP.NET Core** – Different auth strategies:

```csharp
services.AddAuthentication()
    .AddJwtBearer(options => { /* JWT strategy */ })
    .AddCookie(options => { /* Cookie strategy */ })
    .AddGoogle(options => { /* OAuth strategy */ });
```

## When to Use the Strategy Pattern

Use the Strategy Pattern when:

1. **You have multiple algorithms** that do the same thing in different ways (sorting, validation, pricing, etc.)
2. **You need to switch algorithms at runtime** based on configuration or user input
3. **You want to avoid complex conditionals** (long if-else chains or switch statements)
4. **Different clients need different variants** of an algorithm
5. **The algorithm uses data the client shouldn't know about** (encapsulation)

Don't use it when:

1. **You only have one or two algorithms** – it's overkill
2. **The algorithm never changes** – YAGNI applies here
3. **Clients need to understand the differences** between strategies to choose correctly (in that case, consider a different pattern)

## Common Variations

**Strategy with Factory**: Combine with Factory Pattern to create strategies:

```csharp
public interface ICompressionStrategyFactory
{
    ICompressionStrategy Create(CompressionType type);
}
```

**Strategy with Default**: Provide a sensible default:

```csharp
public class Order
{
    private IShippingStrategy _strategy = new StandardShipping();
    
    public void SetShippingStrategy(IShippingStrategy strategy)
    {
        _strategy = strategy ?? new StandardShipping();
    }
}
```

**Strategy with Dependency Injection**: Let your DI container handle it:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddTransient<IShippingStrategy, StandardShipping>();
    services.AddTransient<IPaymentStrategy, StripePaymentStrategy>();
}
```

## Wrapping Up

The Strategy Pattern is about flexibility. Instead of locking your code into a specific algorithm, you define a contract and let implementations vary. This makes your code open for extension but closed for modification – exactly what the Open/Closed Principle demands.

You've now got three related patterns in your toolkit: composition over inheritance (the principle), the Strategy Pattern (selecting behaviors), and SOLID principles (the foundation). These work together to create flexible, maintainable code.

Next time you see a long if-else chain or a complex switch statement handling different cases of the same operation, ask yourself: "Could this be a strategy?" Chances are, the answer is yes. Extract those branches into strategies, and watch your code become easier to test, extend, and reason about.

Remember: the best code is the code you *don't* have to change when requirements shift. The Strategy Pattern helps you get there.
