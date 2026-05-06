---
title: Keyed Services in .NET 8
date: 2026-05-06
tags: dotnet, csharp, aspnetcore, tutorial
image: keyed-services-in-dotnet-8.png
---

.NET 8 quietly added a feature to the dependency injection container that solves an annoying problem: what do you do when you have multiple implementations of the same interface and you need to inject a specific one? Before .NET 8, the answer usually involved a factory class, a third-party library, or a workaround that felt wrong. Now there's a proper built-in solution — keyed services.

## The Problem

Say you're building a notification system that supports multiple channels: email, SMS, and push notifications. You've got a common interface:

```csharp
public interface INotificationSender
{
    Task SendAsync(string recipient, string message);
}
```

And three implementations:

```csharp
public class EmailSender : INotificationSender { ... }
public class SmsSender : INotificationSender { ... }
public class PushSender : INotificationSender { ... }
```

If you register all three the usual way, the container has no idea which one you want when you inject `INotificationSender`. It'll give you the last one registered. That's not great.

The classic workaround was to inject `IEnumerable<INotificationSender>` and pick the right one at runtime — but that leaks implementation knowledge into your consumer code. Or you'd write a factory:

```csharp
public class NotificationSenderFactory
{
    private readonly IEnumerable<INotificationSender> _senders;
    public INotificationSender GetSender(string channel) => ...
}
```

This works, but it's extra ceremony. Keyed services cut straight through it.

## Registering Keyed Services

In .NET 8 you register services with a key — any object, but strings and enums are the most common choices:

```csharp
builder.Services.AddKeyedScoped<INotificationSender, EmailSender>("email");
builder.Services.AddKeyedScoped<INotificationSender, SmsSender>("sms");
builder.Services.AddKeyedScoped<INotificationSender, PushSender>("push");
```

There are keyed variants of all the standard lifetimes: `AddKeyedSingleton`, `AddKeyedScoped`, and `AddKeyedTransient`. They work exactly like their unkeyed equivalents, just with an extra key parameter up front.

## Resolving by Key in Constructors

The cleanest way to consume a keyed service is with the `[FromKeyedServices]` attribute on a constructor parameter:

```csharp
public class OrderNotificationService(
    [FromKeyedServices("email")] INotificationSender emailSender,
    [FromKeyedServices("sms")] INotificationSender smsSender)
{
    public async Task NotifyAsync(Order order)
    {
        await emailSender.SendAsync(order.CustomerEmail, $"Order {order.Id} confirmed");
        
        if (order.SmsOptIn)
            await smsSender.SendAsync(order.CustomerPhone, $"Order {order.Id} confirmed");
    }
}
```

The DI container resolves the right implementation for each parameter. No factory, no `IEnumerable` gymnastics — just a clean constructor that says exactly what it needs.

## Resolving by Key at Runtime

Sometimes you don't know the key until runtime. For that, inject `IServiceProvider` (or `[FromKeyedServices]` on `IServiceProvider` itself isn't needed — just the regular one) and use the `GetKeyedService` extension method:

```csharp
public class DynamicNotificationService(IServiceProvider services)
{
    public async Task SendAsync(string channel, string recipient, string message)
    {
        var sender = services.GetKeyedService<INotificationSender>(channel);
        
        if (sender is null)
            throw new InvalidOperationException($"No sender registered for channel '{channel}'");
        
        await sender.SendAsync(recipient, message);
    }
}
```

There's also `GetRequiredKeyedService<T>` that throws if the key doesn't exist, matching the existing `GetRequiredService<T>` pattern:

```csharp
var sender = services.GetRequiredKeyedService<INotificationSender>("email");
```

## Using Enum Keys

Strings work fine, but enums give you compile-time safety and avoid typos:

```csharp
public enum NotificationChannel { Email, Sms, Push }

builder.Services.AddKeyedScoped<INotificationSender, EmailSender>(NotificationChannel.Email);
builder.Services.AddKeyedScoped<INotificationSender, SmsSender>(NotificationChannel.Sms);
builder.Services.AddKeyedScoped<INotificationSender, PushSender>(NotificationChannel.Push);
```

And resolving with `[FromKeyedServices]`:

```csharp
public class OrderNotificationService(
    [FromKeyedServices(NotificationChannel.Email)] INotificationSender emailSender)
{
    // ...
}
```

The key type just needs to implement equality correctly — enums, strings, and integers all do.

## In Minimal API Endpoints

Minimal APIs support `[FromKeyedServices]` on handler parameters too:

```csharp
app.MapPost("/notify/{channel}", async (
    string channel,
    NotificationRequest request,
    IServiceProvider services) =>
{
    var sender = services.GetKeyedService<INotificationSender>(channel);
    
    if (sender is null)
        return Results.BadRequest($"Unknown channel: {channel}");
    
    await sender.SendAsync(request.Recipient, request.Message);
    return Results.Ok();
});
```

If the key is known at compile time you can use the attribute directly on the parameter instead:

```csharp
app.MapPost("/notify/email", async (
    [FromKeyedServices("email")] INotificationSender sender,
    NotificationRequest request) =>
{
    await sender.SendAsync(request.Recipient, request.Message);
    return Results.Ok();
});
```

## Keyed and Unkeyed Registration Together

You can mix keyed and unkeyed registrations for the same interface. The unkeyed registration is what you get from a plain `INotificationSender` injection; keyed registrations are only resolved when a key is specified:

```csharp
// Default sender for unkeyed injection
builder.Services.AddScoped<INotificationSender, EmailSender>();

// Named senders for keyed injection
builder.Services.AddKeyedScoped<INotificationSender, EmailSender>("email");
builder.Services.AddKeyedScoped<INotificationSender, SmsSender>("sms");
```

This is handy when you have a sensible default but also need to support explicit alternatives in specific places.

## When to Reach for Keyed Services

Keyed services shine in a few recurring scenarios:

- **Multiple implementations of the same interface** — payment gateways, notification channels, storage providers, report formatters
- **Plugin-style architectures** — where implementations are registered by a string key that comes from config or user input
- **Replacing factories** — if you've got a `GetByName` factory method, keyed services probably express the intent more cleanly
- **Multi-tenant scenarios** — where you need per-tenant implementations of shared interfaces

They're not a replacement for strategy pattern or factory pattern in every case — sometimes a factory with real logic is the right tool. But when you're just trying to pick an implementation by name, keyed services are the better fit.

## Wrapping Up

Keyed services in .NET 8 fill a gap that previously required workarounds or third-party libraries. The API is straightforward:

- Register with `AddKeyedScoped` (or `AddKeyedSingleton`/`AddKeyedTransient`)
- Inject with `[FromKeyedServices("key")]` on constructor parameters or handler parameters
- Resolve at runtime with `GetKeyedService<T>("key")` or `GetRequiredKeyedService<T>("key")`

If you've got a factory class in your project whose only job is to pick an implementation by name, it's probably worth swapping it out for keyed services. It's one less thing to maintain, and the intent is clearer in the registration code.
