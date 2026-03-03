---
title: Factory Pattern - Hiding Object Creation
date: 2026-03-03
tags: csharp, design-patterns, factory-pattern, architecture
image: factory-pattern-hiding-object-creation.png
---

We've been working through a design patterns series covering [SOLID principles](solid-principles-foundation-of-good-design.html), the [Strategy Pattern](strategy-pattern-swapping-algorithms-at-runtime.html), [composition over inheritance](composition-over-inheritance-building-flexible-systems.html), and [memoisation](memoisation-caching-expensive-computations.html). Today we're tackling something you hit every time you create an object: the Factory Pattern.

The idea is straightforward. Instead of calling `new SomeConcreteClass()` directly in your business logic, you delegate that decision to a factory. The factory knows *how* to create objects — your calling code just knows *what* it needs. That separation is surprisingly powerful.

## The Problem: Hard-Coded Dependencies

Here's a typical pattern you'll see in real codebases. An order service that sends notifications:

```csharp
public class OrderService
{
    public void ProcessOrder(Order order)
    {
        // ... process the order ...

        var notifier = new EmailNotifier("smtp.company.com", 587);
        notifier.Send(order.Customer.Email, "Your order is confirmed!");
    }
}
```

What's wrong here? `OrderService` is directly coupled to `EmailNotifier`. It knows:

- *That* an email notifier is used
- *How* to construct it (SMTP host, port)
- *Where* to get configuration from

That's three things your `OrderService` shouldn't care about. And when the business decides to also send SMS confirmations, or switch to SendGrid, you're editing `OrderService` — not where that kind of change belongs.

## Simple Factory

The most straightforward fix is to extract the creation logic into a dedicated factory class:

```csharp
public interface INotifier
{
    void Send(string recipient, string message);
}

public class EmailNotifier : INotifier
{
    private readonly string _smtpHost;
    private readonly int _port;

    public EmailNotifier(string smtpHost, int port)
    {
        _smtpHost = smtpHost;
        _port = port;
    }

    public void Send(string recipient, string message)
    {
        // Send email via SMTP
    }
}

public class NotifierFactory
{
    public static INotifier Create()
    {
        return new EmailNotifier("smtp.company.com", 587);
    }
}
```

Now `OrderService` doesn't construct anything directly:

```csharp
public class OrderService
{
    public void ProcessOrder(Order order)
    {
        // ... process the order ...

        var notifier = NotifierFactory.Create();
        notifier.Send(order.Customer.Email, "Your order is confirmed!");
    }
}
```

The creation logic is now in one place. When the SMTP configuration changes, you change the factory. When you switch to SendGrid, you change the factory. `OrderService` stays untouched.

But there's still a problem: `OrderService` is calling `NotifierFactory.Create()` directly. It still has a concrete dependency — on the factory itself. Let's fix that.

## Factory Method Pattern

The Factory Method Pattern moves object creation into the class hierarchy. You define an abstract method for creating objects and let subclasses decide the concrete type:

```csharp
public abstract class OrderProcessor
{
    // Factory method — subclasses decide what notifier to use
    protected abstract INotifier CreateNotifier();

    public void ProcessOrder(Order order)
    {
        // ... process the order ...

        var notifier = CreateNotifier();
        notifier.Send(order.Customer.Email, "Your order is confirmed!");
    }
}

public class EmailOrderProcessor : OrderProcessor
{
    protected override INotifier CreateNotifier()
    {
        return new EmailNotifier("smtp.company.com", 587);
    }
}

public class SmsOrderProcessor : OrderProcessor
{
    protected override INotifier CreateNotifier()
    {
        return new SmsNotifier("+1-800-555-0100");
    }
}
```

The `ProcessOrder` logic is written once in the base class. The decision about *which* notifier to use lives in the subclass. Your core business logic doesn't need to change when you add a new notification channel.

## Abstract Factory: Families of Objects

Sometimes you need to create groups of related objects that belong together. That's where the Abstract Factory Pattern comes in. Instead of a factory that creates one type of object, it creates a whole family.

Say you're building a UI toolkit that needs to render differently on Windows and macOS. Each platform needs its own button, text field, and dialog:

```csharp
// Abstract factory interface
public interface IUIFactory
{
    IButton CreateButton();
    ITextField CreateTextField();
    IDialog CreateDialog();
}

// Platform-specific factories
public class WindowsUIFactory : IUIFactory
{
    public IButton CreateButton() => new WindowsButton();
    public ITextField CreateTextField() => new WindowsTextField();
    public IDialog CreateDialog() => new WindowsDialog();
}

public class MacOSUIFactory : IUIFactory
{
    public IButton CreateButton() => new MacOSButton();
    public ITextField CreateTextField() => new MacOSTextField();
    public IDialog CreateDialog() => new MacOSDialog();
}
```

Your application code works with `IUIFactory` and never knows which platform it's on:

```csharp
public class Application
{
    private readonly IUIFactory _uiFactory;

    public Application(IUIFactory uiFactory)
    {
        _uiFactory = uiFactory;
    }

    public void RenderLoginScreen()
    {
        var usernameField = _uiFactory.CreateTextField();
        var passwordField = _uiFactory.CreateTextField();
        var loginButton = _uiFactory.CreateButton();

        usernameField.Render("Username");
        passwordField.Render("Password", masked: true);
        loginButton.Render("Log In");
    }
}
```

The key insight: all the Windows controls work together, all the macOS controls work together. You can't accidentally mix a `WindowsButton` with a `MacOSTextField` because the factory enforces consistency.

## A Real-World Example: Database Connections

Let me show you something you'll hit in actual .NET projects. You need to support multiple databases — SQL Server in production, SQLite in tests, PostgreSQL for a client running Linux:

```csharp
public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

public class SqlServerConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlServerConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}

public class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _dataSource;

    public SqliteConnectionFactory(string dataSource)
    {
        _dataSource = dataSource;
    }

    public IDbConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_dataSource}");
    }
}
```

Your repository uses the factory, not the concrete connection type:

```csharp
public class ProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Product>(
            "SELECT * FROM Products WHERE Id = @id", new { id });
    }
}
```

In production, inject `SqlServerConnectionFactory`. In tests, inject `SqliteConnectionFactory`. The repository doesn't care which one it gets. This is the Factory Pattern working alongside dependency injection — both patterns complementing each other.

## Dynamic Factories with Registration

Sometimes you don't know at compile time which implementation to create. You need to pick one at runtime based on some key — a config value, a user preference, an API request. Here's a registry-based factory that handles this:

```csharp
public class NotifierFactory
{
    private readonly Dictionary<string, Func<INotifier>> _registry = new();

    public void Register(string key, Func<INotifier> factory)
    {
        _registry[key] = factory;
    }

    public INotifier Create(string key)
    {
        if (!_registry.TryGetValue(key, out var factory))
            throw new ArgumentException($"No notifier registered for key '{key}'");

        return factory();
    }
}
```

Wire it up at startup:

```csharp
var factory = new NotifierFactory();
factory.Register("email", () => new EmailNotifier("smtp.company.com", 587));
factory.Register("sms", () => new SmsNotifier("+1-800-555-0100"));
factory.Register("push", () => new PushNotifier(config.PushApiKey));
```

Use it wherever you need a notifier:

```csharp
var channel = userPreferences.PreferredChannel; // "email", "sms", etc.
var notifier = factory.Create(channel);
notifier.Send(user.ContactInfo, "Your order is confirmed!");
```

Adding a new notification channel? Register it. Nothing else changes.

## Factory Pattern in .NET

The .NET framework uses factories extensively. You're probably already using them:

**`DbProviderFactory`** — the abstract base for creating database objects:

```csharp
DbProviderFactory factory = DbProviderFactories.GetFactory("System.Data.SqlClient");
using var connection = factory.CreateConnection();
using var command = factory.CreateCommand();
```

**`ILoggerFactory`** in Microsoft.Extensions.Logging:

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.AddDebug();
});

var logger = loggerFactory.CreateLogger<OrderService>();
```

**`IHttpClientFactory`**:

```csharp
// Register
services.AddHttpClient("payments", client =>
{
    client.BaseAddress = new Uri("https://api.payments.example.com");
});

// Use
var client = httpClientFactory.CreateClient("payments");
```

Each of these hides complexity behind a factory interface. You call `Create()`, you get back an object ready to use, and you don't care about the details.

## When to Use the Factory Pattern

Use a factory when:

1. **Object creation is complex** — More than just `new SomeClass()`. Configuration, dependencies, initialization steps.
2. **You need to vary the type created** — Different environments, configurations, or runtime decisions.
3. **You want to centralise creation** — Changes to how objects are built go in one place.
4. **You're working with interfaces** — The factory returns an interface; callers don't need to know about concrete types.
5. **Testing requires different implementations** — Swap the production factory for a test factory without touching business logic.

Skip the factory when:

1. **Creation is trivial** — If you're just doing `new Order()` with no complexity, a factory is overkill.
2. **The type never varies** — If there's only ever one implementation and that won't change, you're adding indirection for no benefit.
3. **You already have DI** — Registering types with a dependency injection container often gives you factory-like behaviour without writing a factory explicitly.

## Wrapping Up

The Factory Pattern is about control. You control where object creation happens, what gets created, and how it's configured. Your calling code deals with contracts (interfaces), not implementations (concrete classes).

We've seen three flavours here: the Simple Factory (centralise creation logic), the Factory Method Pattern (let subclasses decide what to create), and the Abstract Factory (create families of related objects). You won't need all three on every project, but you'll reach for at least one of them constantly.

This pattern pairs naturally with almost everything else in this series. It complements the [Strategy Pattern](strategy-pattern-swapping-algorithms-at-runtime.html) (factories choose which strategy to create), the [SOLID principles](solid-principles-foundation-of-good-design.html) (especially Dependency Inversion), and [composition over inheritance](composition-over-inheritance-building-flexible-systems.html) (factories compose implementations behind interfaces).

Next time you find yourself writing `new SomeConcreteClass()` deep inside business logic, ask: should this creation live here? If the answer is no — and often it isn't — you know what to do.
