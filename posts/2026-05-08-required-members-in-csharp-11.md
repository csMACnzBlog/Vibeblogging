---
title: Required Members in C# 11
date: 2026-05-08
tags: csharp, dotnet, tutorial
image: required-members-in-csharp-11.png
---

If you've ever created a model with object initialisers and then realised someone forgot to set a critical property, C# 11's `required` keyword is the fix you've wanted for years. It lets you keep object initialiser ergonomics while making sure important members are always set.

## The Problem with Optional-by-Default Properties

Before `required`, this was valid C#:

```csharp
public class UserRegistration
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

var registration = new UserRegistration(); // Oops: no Email, no Password
```

The compiler couldn't tell that `Email` and `Password` are business-critical. You either relied on runtime validation or moved to constructors everywhere.

## Marking Members as Required

Now you can declare intent directly:

```csharp
public class UserRegistration
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public string? ReferralCode { get; init; }
}
```

And the compiler enforces it:

```csharp
var ok = new UserRegistration
{
    Email = "dev@example.com",
    Password = "correct horse battery staple"
};

var broken = new UserRegistration
{
    Email = "dev@example.com"
};
// CS9035: Required member 'UserRegistration.Password' must be set
```

That check happens at compile time, which means fewer “this should never be null” failures slipping into runtime.

## Required + init Is a Great Pair

In practice, `required` is most useful with `init` properties:

```csharp
public class ApiClientOptions
{
    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
```

You get immutable-style setup with explicit mandatory fields. Callers can still use object initialisers, but they can't accidentally skip essential configuration.

## Constructors and `SetsRequiredMembers`

If you set all required members inside a constructor, tell the compiler using `[SetsRequiredMembers]`:

```csharp
using System.Diagnostics.CodeAnalysis;

public class User
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    [SetsRequiredMembers]
    public User(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}

var user = new User("u-123", "Chris"); // No object initialiser needed
```

Without the attribute, the compiler assumes required members might still be unset after constructor execution.

## Inheritance Rules to Know

Required members participate in inheritance, so derived types must satisfy base requirements too:

```csharp
public class Entity
{
    public required Guid Id { get; init; }
}

public class Order : Entity
{
    public required decimal Total { get; init; }
}

var order = new Order
{
    Id = Guid.NewGuid(),
    Total = 149.99m
};
```

If you omit `Id`, you'll get a compiler error even though it comes from the base class.

## A Practical Pattern for DTOs

A nice real-world use is request/command DTOs where some fields are always mandatory:

```csharp
public sealed record CreateInvoiceRequest
{
    public required string CustomerId { get; init; }
    public required IReadOnlyList<InvoiceLine> Lines { get; init; }
    public string Currency { get; init; } = "USD";
}

public sealed record InvoiceLine
{
    public required string Description { get; init; }
    public required decimal Amount { get; init; }
}
```

This keeps your models concise and self-documenting. The compiler becomes a teammate that catches missing data before the app even starts.

## Wrapping Up

`required` gives you a better default for object construction in modern C#:

- Keep object initialisers (readable and flexible)
- Enforce mandatory members at compile time
- Combine with `init` for safer immutable-style models
- Use `[SetsRequiredMembers]` when constructors set everything

If your codebase still relies on comments like “don't forget to set X”, this feature is an easy upgrade. Let the compiler do that remembering for you.
