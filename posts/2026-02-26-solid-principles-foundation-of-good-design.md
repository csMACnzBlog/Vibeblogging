---
title: "SOLID Principles: Foundation of Good Design"
date: 2026-02-26
tags: csharp, solid, architecture, design-patterns, best-practices
image: solid-principles-foundation-of-good-design.png
---

# SOLID Principles: Foundation of Good Design

In the [last post](design-patterns-series-composition-over-complexity.html), we talked about breaking down complex code into small, composable pieces. We saw how a massive `ProcessOrder` method could be refactored into focused components. But *why* did that refactoring work? What principles guided those decisions?

That's where SOLID comes in. These five principles form the foundation of good object-oriented design. They're not just academic theory—they're practical guidelines that'll help you write cleaner, more maintainable C# code.

Let's dive into each principle with real C# examples you can apply immediately.

## What is SOLID?

SOLID is an acronym for five design principles:

- **S**ingle Responsibility Principle
- **O**pen/Closed Principle
- **L**iskov Substitution Principle
- **I**nterface Segregation Principle
- **D**ependency Inversion Principle

Each principle addresses a specific problem in software design. Together, they help you write code that's easier to test, maintain, and extend.

## Single Responsibility Principle (SRP)

**A class should have one, and only one, reason to change.**

This is the principle we demonstrated in the last post. When a class does multiple things, changes to one responsibility can break another. Let's look at a simple example.

### The Problem

Here's a class that does too much:

```csharp
public class UserService
{
    public void RegisterUser(string email, string password)
    {
        // Validate email format
        if (!email.Contains("@"))
            throw new ArgumentException("Invalid email");
        
        // Hash password
        var hashedPassword = BCrypt.HashPassword(password);
        
        // Save to database
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        var command = new SqlCommand(
            "INSERT INTO Users (Email, Password) VALUES (@email, @password)",
            connection);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@password", hashedPassword);
        command.ExecuteNonQuery();
        
        // Send welcome email
        var emailClient = new SmtpClient("smtp.example.com");
        emailClient.Send(email, "Welcome!", "Thanks for signing up!");
        
        // Log the event
        File.AppendAllText("users.log", $"{DateTime.Now}: User {email} registered\n");
    }
}
```

This class has *five* reasons to change:
1. Validation rules change
2. Password hashing algorithm changes
3. Database schema changes
4. Email provider changes
5. Logging mechanism changes

That's five potential bugs waiting to happen.

### The Solution

Let's break this into focused classes:

```csharp
public class UserValidator
{
    public void ValidateEmail(string email)
    {
        if (!email.Contains("@"))
            throw new ArgumentException("Invalid email");
    }
}

public class PasswordHasher
{
    public string Hash(string password) => BCrypt.HashPassword(password);
}

public class UserRepository
{
    private readonly string _connectionString;
    
    public void Save(User user)
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        var command = new SqlCommand(
            "INSERT INTO Users (Email, Password) VALUES (@email, @password)",
            connection);
        command.Parameters.AddWithValue("@email", user.Email);
        command.Parameters.AddWithValue("@password", user.HashedPassword);
        command.ExecuteNonQuery();
    }
}

public class EmailService
{
    public void SendWelcomeEmail(string email)
    {
        var emailClient = new SmtpClient("smtp.example.com");
        emailClient.Send(email, "Welcome!", "Thanks for signing up!");
    }
}

public class UserLogger
{
    public void LogRegistration(string email)
    {
        File.AppendAllText("users.log", 
            $"{DateTime.Now}: User {email} registered\n");
    }
}

public class UserService
{
    private readonly UserValidator _validator;
    private readonly PasswordHasher _hasher;
    private readonly UserRepository _repository;
    private readonly EmailService _emailService;
    private readonly UserLogger _logger;
    
    public UserService(
        UserValidator validator,
        PasswordHasher hasher,
        UserRepository repository,
        EmailService emailService,
        UserLogger logger)
    {
        _validator = validator;
        _hasher = hasher;
        _repository = repository;
        _emailService = emailService;
        _logger = logger;
    }
    
    public void RegisterUser(string email, string password)
    {
        _validator.ValidateEmail(email);
        var hashedPassword = _hasher.Hash(password);
        var user = new User { Email = email, HashedPassword = hashedPassword };
        _repository.Save(user);
        _emailService.SendWelcomeEmail(email);
        _logger.LogRegistration(email);
    }
}
```

Now each class has one clear responsibility. Changes to email validation won't affect database access. Changes to logging won't break password hashing. You can test each piece in isolation.

## Open/Closed Principle (OCP)

**Software entities should be open for extension, but closed for modification.**

In other words, you should be able to add new functionality without changing existing code. This prevents you from breaking working code when adding features.

### The Problem

Here's a discount calculator that violates OCP:

```csharp
public class DiscountCalculator
{
    public decimal CalculateDiscount(Customer customer, decimal amount)
    {
        if (customer.Type == CustomerType.Regular)
        {
            return amount * 0.05m; // 5% discount
        }
        else if (customer.Type == CustomerType.Premium)
        {
            return amount * 0.10m; // 10% discount
        }
        else if (customer.Type == CustomerType.VIP)
        {
            return amount * 0.20m; // 20% discount
        }
        
        return 0;
    }
}
```

Want to add a new customer type? You'll need to modify this method. That means retesting everything and risking breaking existing discounts.

### The Solution

Use polymorphism and abstraction:

```csharp
public interface IDiscountStrategy
{
    decimal Calculate(decimal amount);
}

public class RegularCustomerDiscount : IDiscountStrategy
{
    public decimal Calculate(decimal amount) => amount * 0.05m;
}

public class PremiumCustomerDiscount : IDiscountStrategy
{
    public decimal Calculate(decimal amount) => amount * 0.10m;
}

public class VIPCustomerDiscount : IDiscountStrategy
{
    public decimal Calculate(decimal amount) => amount * 0.20m;
}

public class Customer
{
    public IDiscountStrategy DiscountStrategy { get; set; }
}

public class DiscountCalculator
{
    public decimal CalculateDiscount(Customer customer, decimal amount)
    {
        return customer.DiscountStrategy.Calculate(amount);
    }
}
```

Now adding a new discount type is simple—just create a new class that implements `IDiscountStrategy`. The `DiscountCalculator` doesn't need to change at all. It's closed for modification but open for extension.

Want to add a seasonal discount? No problem:

```csharp
public class SeasonalDiscount : IDiscountStrategy
{
    private readonly decimal _rate;
    
    public SeasonalDiscount(decimal rate)
    {
        _rate = rate;
    }
    
    public decimal Calculate(decimal amount) => amount * _rate;
}
```

Zero changes to existing code. That's OCP in action.

## Liskov Substitution Principle (LSP)

**Objects of a superclass should be replaceable with objects of a subclass without breaking the application.**

This means subclasses should enhance, not break, the behavior of their base classes. If a method expects a `Bird`, it should work with any type of `Bird` without surprises.

### The Problem

Here's a classic violation:

```csharp
public class Bird
{
    public virtual void Fly()
    {
        Console.WriteLine("Flying through the sky!");
    }
}

public class Sparrow : Bird
{
    public override void Fly()
    {
        Console.WriteLine("Sparrow flying!");
    }
}

public class Penguin : Bird
{
    public override void Fly()
    {
        throw new NotSupportedException("Penguins can't fly!");
    }
}

public class BirdWatcher
{
    public void WatchBird(Bird bird)
    {
        bird.Fly(); // Boom! Crashes if bird is a Penguin
    }
}
```

A `Penguin` is a `Bird`, but substituting it for a `Bird` breaks our code. That violates LSP.

### The Solution

Don't model the world incorrectly. Not all birds fly, so `Fly` shouldn't be on the base `Bird` class:

```csharp
public abstract class Bird
{
    public abstract void Move();
}

public interface IFlyable
{
    void Fly();
}

public class Sparrow : Bird, IFlyable
{
    public override void Move()
    {
        Fly();
    }
    
    public void Fly()
    {
        Console.WriteLine("Sparrow flying!");
    }
}

public class Penguin : Bird
{
    public override void Move()
    {
        Console.WriteLine("Penguin waddling!");
    }
}

public class BirdWatcher
{
    public void WatchBird(Bird bird)
    {
        bird.Move(); // Works for all birds
    }
    
    public void WatchFlyingBird(IFlyable flyingBird)
    {
        flyingBird.Fly(); // Only accepts birds that can actually fly
    }
}
```

Now any `Bird` can be substituted safely. If you need a bird that flies, use `IFlyable`. The type system prevents you from passing a `Penguin` to `WatchFlyingBird`.

Here's a more practical example with rectangles:

```csharp
// Before: Violates LSP
public class Rectangle
{
    public virtual int Width { get; set; }
    public virtual int Height { get; set; }
    
    public int CalculateArea() => Width * Height;
}

public class Square : Rectangle
{
    public override int Width
    {
        get => base.Width;
        set
        {
            base.Width = value;
            base.Height = value; // Side effect! Breaks expectations
        }
    }
    
    public override int Height
    {
        get => base.Height;
        set
        {
            base.Width = value;
            base.Height = value; // Another side effect!
        }
    }
}

// This breaks with Square:
var rect = new Square();
rect.Width = 5;
rect.Height = 10;
// Expected: 50, Actual: 100 (because setting Height also changed Width)
```

The fix? Don't make `Square` inherit from `Rectangle`:

```csharp
public interface IShape
{
    int CalculateArea();
}

public class Rectangle : IShape
{
    public int Width { get; set; }
    public int Height { get; set; }
    
    public int CalculateArea() => Width * Height;
}

public class Square : IShape
{
    public int Side { get; set; }
    
    public int CalculateArea() => Side * Side;
}
```

Now there's no inheritance relationship that makes false promises. Both shapes implement `IShape`, but each has its own properties that make sense.

## Interface Segregation Principle (ISP)

**No client should be forced to depend on methods it does not use.**

Keep interfaces small and focused. Don't create fat interfaces that force classes to implement methods they don't need.

### The Problem

Here's a chunky interface:

```csharp
public interface IWorker
{
    void Work();
    void Eat();
    void Sleep();
    void GetPaid();
}

public class HumanWorker : IWorker
{
    public void Work() => Console.WriteLine("Working hard!");
    public void Eat() => Console.WriteLine("Eating lunch!");
    public void Sleep() => Console.WriteLine("Sleeping at night!");
    public void GetPaid() => Console.WriteLine("Getting paid!");
}

public class RobotWorker : IWorker
{
    public void Work() => Console.WriteLine("Working 24/7!");
    public void Eat() => throw new NotImplementedException("Robots don't eat!");
    public void Sleep() => throw new NotImplementedException("Robots don't sleep!");
    public void GetPaid() => throw new NotImplementedException("Robots don't get paid!");
}
```

Robots don't eat or sleep, but they're forced to implement those methods. That's ISP violation.

### The Solution

Split the fat interface into smaller, focused ones:

```csharp
public interface IWorkable
{
    void Work();
}

public interface IFeedable
{
    void Eat();
}

public interface ISleepable
{
    void Sleep();
}

public interface IPayable
{
    void GetPaid();
}

public class HumanWorker : IWorkable, IFeedable, ISleepable, IPayable
{
    public void Work() => Console.WriteLine("Working hard!");
    public void Eat() => Console.WriteLine("Eating lunch!");
    public void Sleep() => Console.WriteLine("Sleeping at night!");
    public void GetPaid() => Console.WriteLine("Getting paid!");
}

public class RobotWorker : IWorkable
{
    public void Work() => Console.WriteLine("Working 24/7!");
}

public class ContractorWorker : IWorkable, IPayable
{
    public void Work() => Console.WriteLine("Working on contract!");
    public void GetPaid() => Console.WriteLine("Getting paid per project!");
}
```

Now each class implements only what it needs. Want a worker that can work? Use `IWorkable`. Need to feed something? Use `IFeedable`. No more forcing robots to implement eating methods.

Here's a real-world example with data access:

```csharp
// Before: Fat interface
public interface IRepository<T>
{
    T GetById(int id);
    IEnumerable<T> GetAll();
    void Add(T entity);
    void Update(T entity);
    void Delete(int id);
    IEnumerable<T> Search(string query);
    void BulkInsert(IEnumerable<T> entities);
    void Archive(int id);
}

// Some repositories don't support archiving or bulk operations
public class ReadOnlyProductRepository : IRepository<Product>
{
    public Product GetById(int id) { /* ... */ }
    public IEnumerable<Product> GetAll() { /* ... */ }
    
    // Forced to implement these even though we're read-only:
    public void Add(Product entity) => throw new NotSupportedException();
    public void Update(Product entity) => throw new NotSupportedException();
    public void Delete(int id) => throw new NotSupportedException();
    public IEnumerable<Product> Search(string query) => throw new NotSupportedException();
    public void BulkInsert(IEnumerable<Product> entities) => throw new NotSupportedException();
    public void Archive(int id) => throw new NotSupportedException();
}
```

Better approach:

```csharp
public interface IReadRepository<T>
{
    T GetById(int id);
    IEnumerable<T> GetAll();
}

public interface IWriteRepository<T>
{
    void Add(T entity);
    void Update(T entity);
    void Delete(int id);
}

public interface ISearchableRepository<T>
{
    IEnumerable<T> Search(string query);
}

public interface IBulkRepository<T>
{
    void BulkInsert(IEnumerable<T> entities);
}

public interface IArchivableRepository<T>
{
    void Archive(int id);
}

// Now implement only what you need:
public class ReadOnlyProductRepository : IReadRepository<Product>
{
    public Product GetById(int id) { /* ... */ }
    public IEnumerable<Product> GetAll() { /* ... */ }
}

public class FullProductRepository : 
    IReadRepository<Product>, 
    IWriteRepository<Product>, 
    ISearchableRepository<Product>,
    IBulkRepository<Product>,
    IArchivableRepository<Product>
{
    // Implements all the things
}
```

Each interface is small and focused. Classes only implement what they actually support.

## Dependency Inversion Principle (DIP)

**High-level modules should not depend on low-level modules. Both should depend on abstractions.**

This is about decoupling your code. Don't create concrete dependencies directly—depend on interfaces or abstractions instead.

### The Problem

Here's tightly coupled code:

```csharp
public class EmailNotification
{
    public void Send(string message)
    {
        var smtp = new SmtpClient("smtp.example.com");
        smtp.Send("user@example.com", "Notification", message);
    }
}

public class OrderProcessor
{
    public void ProcessOrder(Order order)
    {
        // Process the order...
        
        // Send notification
        var notification = new EmailNotification();
        notification.Send($"Order {order.Id} processed");
    }
}
```

`OrderProcessor` directly creates an `EmailNotification`. What if you want to send SMS notifications instead? Or push notifications? You'll need to modify `OrderProcessor`. That's tight coupling.

### The Solution

Depend on abstractions:

```csharp
public interface INotificationService
{
    void Send(string message);
}

public class EmailNotification : INotificationService
{
    public void Send(string message)
    {
        var smtp = new SmtpClient("smtp.example.com");
        smtp.Send("user@example.com", "Notification", message);
    }
}

public class SmsNotification : INotificationService
{
    public void Send(string message)
    {
        var smsClient = new TwilioClient();
        smsClient.SendSms("+1234567890", message);
    }
}

public class PushNotification : INotificationService
{
    public void Send(string message)
    {
        var pushService = new FirebaseCloudMessaging();
        pushService.SendPush("device-token", message);
    }
}

public class OrderProcessor
{
    private readonly INotificationService _notificationService;
    
    public OrderProcessor(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }
    
    public void ProcessOrder(Order order)
    {
        // Process the order...
        
        // Send notification (doesn't care which type)
        _notificationService.Send($"Order {order.Id} processed");
    }
}
```

Now `OrderProcessor` depends on the `INotificationService` abstraction, not concrete implementations. You can swap notification methods without touching `OrderProcessor`. You can even combine multiple notifications:

```csharp
public class CompositeNotification : INotificationService
{
    private readonly IEnumerable<INotificationService> _services;
    
    public CompositeNotification(IEnumerable<INotificationService> services)
    {
        _services = services;
    }
    
    public void Send(string message)
    {
        foreach (var service in _services)
        {
            service.Send(message);
        }
    }
}

// Usage: Send both email and SMS
var notification = new CompositeNotification(new INotificationService[]
{
    new EmailNotification(),
    new SmsNotification()
});

var processor = new OrderProcessor(notification);
```

DIP also makes testing easier:

```csharp
public class FakeNotification : INotificationService
{
    public List<string> SentMessages { get; } = new();
    
    public void Send(string message)
    {
        SentMessages.Add(message);
    }
}

// In your tests:
var fakeNotification = new FakeNotification();
var processor = new OrderProcessor(fakeNotification);

processor.ProcessOrder(testOrder);

Assert.Single(fakeNotification.SentMessages);
Assert.Contains("Order 123 processed", fakeNotification.SentMessages[0]);
```

No need to send actual emails in your tests. That's the power of DIP.

## Putting It All Together

Let's see how all five principles work together in a real scenario. Here's a payment processing system:

```csharp
// SRP: Each class has one responsibility
public interface IPaymentValidator
{
    void Validate(PaymentRequest request);
}

public interface IPaymentGateway
{
    PaymentResult Process(PaymentRequest request);
}

public interface IPaymentLogger
{
    void LogSuccess(PaymentRequest request, PaymentResult result);
    void LogFailure(PaymentRequest request, string error);
}

public interface IPaymentNotifier
{
    void NotifySuccess(PaymentRequest request);
    void NotifyFailure(PaymentRequest request, string error);
}

// OCP: Open for extension (new payment gateways)
public class PayPalPaymentGateway : IPaymentGateway
{
    public PaymentResult Process(PaymentRequest request)
    {
        // PayPal-specific implementation
        return new PaymentResult { Success = true };
    }
}

// ISP: Small, focused interfaces (not all gateways support refunds)
public interface IRefundablePayment
{
    RefundResult Refund(string transactionId, decimal amount);
}

// Stripe supports both payment and refunds
public class StripePaymentGateway : IPaymentGateway, IRefundablePayment
{
    public PaymentResult Process(PaymentRequest request)
    {
        // Stripe-specific implementation
        return new PaymentResult { Success = true };
    }
    
    public RefundResult Refund(string transactionId, decimal amount)
    {
        // Stripe supports refunds
        return new RefundResult { Success = true };
    }
}

// LSP: Any IPaymentGateway works the same way
public class PaymentProcessor
{
    private readonly IPaymentValidator _validator;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentLogger _logger;
    private readonly IPaymentNotifier _notifier;
    
    // DIP: Depend on abstractions, not concrete implementations
    public PaymentProcessor(
        IPaymentValidator validator,
        IPaymentGateway gateway,
        IPaymentLogger logger,
        IPaymentNotifier notifier)
    {
        _validator = validator;
        _gateway = gateway;
        _logger = logger;
        _notifier = notifier;
    }
    
    public PaymentResult ProcessPayment(PaymentRequest request)
    {
        try
        {
            _validator.Validate(request);
            
            var result = _gateway.Process(request);
            
            if (result.Success)
            {
                _logger.LogSuccess(request, result);
                _notifier.NotifySuccess(request);
            }
            else
            {
                _logger.LogFailure(request, result.ErrorMessage);
                _notifier.NotifyFailure(request, result.ErrorMessage);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogFailure(request, ex.Message);
            _notifier.NotifyFailure(request, ex.Message);
            throw;
        }
    }
}
```

Look at what we've achieved:

- **SRP**: Each class has one job—validation, processing, logging, or notification
- **OCP**: Add new payment gateways without modifying `PaymentProcessor`
- **LSP**: Any `IPaymentGateway` implementation works correctly
- **ISP**: `IRefundablePayment` is separate—not all gateways need it
- **DIP**: `PaymentProcessor` depends on interfaces, not concrete classes

This design is testable, maintainable, and extensible. That's SOLID in action.

## When to Apply SOLID

Here's the thing—you don't need to apply all five principles to every single class. SOLID principles are guidelines, not laws. Use them when they add value.

**Apply SOLID when:**
- You're building a system that'll grow over time
- Multiple people will work on the codebase
- You need to test components in isolation
- Requirements change frequently
- You're working on core business logic

**Don't overthink it when:**
- Writing simple scripts or tools
- Building throwaway prototypes
- The class is truly simple and won't change (though be careful with this assumption)
- You're working on a small, short-lived project

The goal isn't perfect adherence to SOLID. The goal is maintainable code that solves real problems.

## Common Pitfalls

As you apply SOLID principles, watch out for these traps:

**Over-abstraction**: Don't create interfaces for everything "just in case." If there's only one implementation and no plans for more, you might not need an interface yet. (That said, testing is a valid reason for abstraction.)

**Premature optimization**: Don't split classes apart before you understand the domain. Sometimes it's better to start simple and refactor when patterns emerge.

**Analysis paralysis**: Don't spend hours debating which principle applies. Make a decision, write tests, and refactor if needed.

**Rigid interfaces**: Don't be afraid to change interfaces as you learn more about the domain. If an interface isn't working, fix it.

## What's Next

SOLID principles give you the foundation, but there's more to learn. In the next posts in this series, we'll explore specific patterns that build on these principles:

- **Composition over Inheritance** - How to build flexible systems without deep inheritance trees
- **Strategy Pattern** - Swapping algorithms at runtime (which we touched on with discounts)
- **Decorator Pattern** - Adding behavior without modification (pure OCP)
- **Dependency Injection** - Practical ways to implement DIP in .NET

Each pattern is a concrete application of one or more SOLID principles. Once you understand SOLID, the patterns will make more sense.

## Key Takeaways

Let's recap the five principles:

1. **SRP**: One class, one responsibility, one reason to change
2. **OCP**: Extend behavior without modifying existing code
3. **LSP**: Subclasses should work wherever the base class works
4. **ISP**: Small, focused interfaces—don't force clients to depend on what they don't use
5. **DIP**: Depend on abstractions, not concrete implementations

These principles work together to create flexible, maintainable code. They're not academic theory—they're practical tools that'll save you from debugging nightmares at 2 AM.

Start with SRP. Once you're comfortable breaking classes into single responsibilities, the other principles will follow naturally. You'll find yourself creating abstractions, splitting interfaces, and injecting dependencies without even thinking about it.

That's when you know SOLID has become second nature.
