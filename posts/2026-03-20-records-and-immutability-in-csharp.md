---
title: C# Records and Immutability
date: 2026-03-20
tags: dotnet, csharp, language-features, records, immutability
image: records-and-immutability-in-csharp.png
---

If you've written DTOs, value objects, or event payloads in C#, you know the boilerplate drill: define a class, add properties, override `Equals`, override `GetHashCode`, maybe implement `IEquatable\<T\>`, write a constructor. It's a lot of ceremony for something conceptually simple. C# 9 introduced records to cut through all of that.

## What Records Are (and Why They Exist)

A record is a reference type optimised for representing data. The key differences from a regular class are:

- **Value equality by default** — two records are equal if their properties are equal, not if they point to the same object
- **Immutability by default** — properties use `init` accessors, so they can only be set during construction
- **Concise syntax** — you can define a full record with one line

Here's the before and after. First, the class version of a simple DTO:

```csharp
public class PersonDto
{
    public string FirstName { get; }
    public string LastName { get; }
    public int Age { get; }

    public PersonDto(string firstName, string lastName, int age)
    {
        FirstName = firstName;
        LastName = lastName;
        Age = age;
    }

    public override bool Equals(object? obj)
        => obj is PersonDto other
            && FirstName == other.FirstName
            && LastName == other.LastName
            && Age == other.Age;

    public override int GetHashCode()
        => HashCode.Combine(FirstName, LastName, Age);

    public override string ToString()
        => $"PersonDto {{ FirstName = {FirstName}, LastName = {LastName}, Age = {Age} }}";
}
```

Now the record version:

```csharp
public record PersonDto(string FirstName, string LastName, int Age);
```

One line. Same semantics. That's the pitch.

## Basic Record Syntax

The one-liner above is the *positional* record syntax — we'll get to that in a moment. You can also define records in a more class-like style if you want explicit control:

```csharp
public record Person
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public int Age { get; init; }
}
```

The `init` accessor is what makes these properties settable during object initialisation but read-only after:

```csharp
var person = new Person { FirstName = "Alice", LastName = "Smith", Age = 30 };

person.Age = 31; // Compile error — init-only property
```

You get value equality for free:

```csharp
var a = new Person { FirstName = "Alice", LastName = "Smith", Age = 30 };
var b = new Person { FirstName = "Alice", LastName = "Smith", Age = 30 };

Console.WriteLine(a == b);        // True
Console.WriteLine(a.Equals(b));   // True
Console.WriteLine(ReferenceEquals(a, b)); // False — different objects
```

This is fundamentally different from classes, where `==` compares references by default.

## Positional Records

Positional records use a shorthand syntax where you define the properties directly in the record declaration, similar to a primary constructor. The compiler generates the constructor, the `Deconstruct` method, and all the equality infrastructure for you:

```csharp
public record Point(double X, double Y);
public record Colour(byte R, byte G, byte B);
```

You construct them just like you'd call a constructor:

```csharp
var origin = new Point(0, 0);
var red = new Colour(255, 0, 0);
```

And because records generate `Deconstruct`, you can use positional deconstruction:

```csharp
var (x, y) = origin;
Console.WriteLine($"x={x}, y={y}"); // x=0, y=0
```

You can mix positional syntax with extra members when you need them:

```csharp
public record Point(double X, double Y)
{
    public double DistanceTo(Point other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
```

## `with` Expressions

Immutability is great in theory, but at some point you need a slightly modified copy. That's what `with` expressions are for — non-destructive mutation:

```csharp
public record Address(string Street, string City, string PostCode);

var original = new Address("10 High Street", "London", "SW1A 1AA");
var moved    = original with { City = "Manchester", PostCode = "M1 1AB" };

Console.WriteLine(original); // Address { Street = 10 High Street, City = London, PostCode = SW1A 1AA }
Console.WriteLine(moved);    // Address { Street = 10 High Street, City = Manchester, PostCode = M1 1AB }
```

`original` is untouched. `moved` is a new instance with your changes applied. The compiler generates a `Clone` method under the hood and copies all properties over, then applies your overrides.

This pattern is particularly useful in immutable domain models. Imagine processing an order through stages:

```csharp
public record Order(int Id, string Status, decimal Total, DateTimeOffset? ShippedAt = null);

var placed   = new Order(1, "Placed", 49.99m);
var paid     = placed  with { Status = "Paid" };
var shipped  = paid    with { Status = "Shipped", ShippedAt = DateTimeOffset.UtcNow };
```

Each step produces a new record. You could stash all three and have a full history with no extra work.

## Value Equality in Practice

Records override `Equals`, `GetHashCode`, and `==`/`!=` based on all their properties. This makes them ideal as dictionary keys and set members — something you'd have to implement manually for classes:

```csharp
public record ProductCode(string Category, string Sku);

var prices = new Dictionary<ProductCode, decimal>
{
    [new ProductCode("ELEC", "TV-4K-55")] = 799.99m,
    [new ProductCode("ELEC", "HDMI-2M")]  = 12.99m,
};

var lookup = new ProductCode("ELEC", "TV-4K-55");
Console.WriteLine(prices[lookup]); // 799.99 — works because equality is by value
```

With a class, that lookup would fail because `new ProductCode(...)` creates a different reference each time.

## Record Structs (C# 10)

C# 10 added `record struct` — everything you love about records, but as a value type. This means they live on the stack (when local variables) and are copied by value, which can matter for performance-sensitive code:

```csharp
public record struct Coordinate(double Lat, double Lng);
```

There's also `readonly record struct` if you want to enforce that the struct itself can't be mutated (recommended to avoid accidental copies):

```csharp
public readonly record struct Temperature(double Celsius)
{
    public double Fahrenheit => Celsius * 9.0 / 5.0 + 32;
}

var boiling = new Temperature(100);
var inF = boiling.Fahrenheit; // 212
```

Use `record struct` when you're dealing with small data (a few fields), you create a lot of them, and allocations are a concern. The coordinate above — if you're processing millions of GPS points — is a good candidate.

## Inheritance with Records

Records support inheritance, though it's best used sparingly. A derived record adds extra properties:

```csharp
public record Shape(string Colour);
public record Circle(string Colour, double Radius) : Shape(Colour);
public record Rectangle(string Colour, double Width, double Height) : Shape(Colour);
```

Equality respects the runtime type — a `Circle` never equals a `Rectangle` even if they have the same colour. The `ToString` output also includes the actual type name, which is useful for debugging.

One caveat: `record struct` can't be inherited. Only `record` (reference type) supports it.

## When to Use Records vs Classes

Here's a practical guide:

**Use a record when:**
- The type represents data, not behaviour
- You want equality based on values, not identity
- You're writing DTOs, query results, events, or value objects
- You want immutability by default with the option to `with`-copy

**Use a class when:**
- The type has significant mutable state that changes over its lifetime
- Identity matters — you need to distinguish between two objects with the same data (e.g., two users with the same name are different people)
- You're implementing patterns where mutation is intentional (e.g., a builder, a cache, a connection)

A good rule of thumb: if you'd describe the type by listing its data ("a point is an X and a Y"), it's probably a record. If you'd describe it by what it does ("a repository manages database access"), it's probably a class.

## A Real-World Example

Here's records applied to a small event-sourcing scenario. Events are perfect records — they happened, they don't change:

```csharp
public abstract record DomainEvent(Guid AggregateId, DateTimeOffset OccurredAt);

public record AccountOpened(
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    string OwnerName,
    decimal InitialDeposit
) : DomainEvent(AggregateId, OccurredAt);

public record MoneyDeposited(
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    decimal Amount
) : DomainEvent(AggregateId, OccurredAt);

public record MoneyWithdrawn(
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    decimal Amount
) : DomainEvent(AggregateId, OccurredAt);
```

When you replay these events to rebuild state, each one is a `with`-copyable, value-equal, deconstructable record. No boilerplate, no surprises.

## Wrapping Up

Records are one of those features that, once you've used them, you wonder how you managed without. They're not a replacement for classes — they're a complement. Keep mutable, behaviour-rich objects as classes. Reach for records whenever you're modelling data that flows through your system rather than lives in it.

The combination of concise syntax, structural equality, and `with` expressions makes records ideal for the data-heavy parts of any C# codebase: your DTOs, your domain events, your query models. Give them a try and watch the boilerplate melt away.
