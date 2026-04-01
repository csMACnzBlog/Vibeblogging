---
title: C# 15 Union Types: Discriminated Unions Arrive
date: 2026-04-01
tags: dotnet, csharp, csharp15, dotnet11, unions
image: csharp-15-union-types.png
---

C# developers have wanted discriminated unions for years. There are whole libraries built around the gap — `OneOf`, `ErrorOr`, `LanguageExt` — because the language just didn't have a native answer. That changes with C# 15 and .NET 11. Union types are here, and they're surprisingly clean.

## What's the Problem?

A discriminated union is a value that's exactly one of a fixed set of types. Not a base class with subclasses scattered across your codebase. Not an interface that anything can implement. A *closed* set, known at compile time.

F# has had this forever:

```fsharp
type Pet =
    | Cat of Cat
    | Dog of Dog
    | Bird of Bird
```

TypeScript has union types too:

```typescript
type Pet = Cat | Dog | Bird;
```

In C#, you've been faking it. The cleanest workaround is abstract records:

```csharp
public abstract record Pet;
public record Cat(string Name) : Pet;
public record Dog(string Name) : Pet;
public record Bird(string Name) : Pet;
```

It works, but the compiler doesn't know the hierarchy is closed. You always need a `_` fallback in your switch expressions, even when you've covered every case. Third-party libraries help with the ergonomics, but you're still fighting the type system rather than working with it.

## The `union` Keyword

C# 15 adds a first-class `union` keyword:

```csharp
public union Pet(Cat, Dog, Bird);
```

That one line declares a union type with three cases. The compiler generates a struct with the `[Union]` attribute, the `IUnion` interface, constructors for each case, and an `object? Value` property that holds the current value.

You can use it immediately:

```csharp
Pet pet = new Dog("Rex");
```

No explicit cast needed. Implicit conversions are generated from each case type to the union. Assign a `Cat`, a `Dog`, or a `Bird` directly.

Nullable unions work as you'd expect:

```csharp
Pet? maybePet = null;
maybePet = new Cat("Whiskers");
```

## Exhaustive Pattern Matching

Here's where it gets good. Switch expressions over union types are exhaustive — the compiler knows all the possible cases:

```csharp
string description = pet switch
{
    Cat c => $"{c.Name} is a cat",
    Dog d => $"{d.Name} is a good dog",
    Bird b => $"{b.Name} can fly"
};
```

No `_ =>` fallback. If you add a fourth case to the union and forget to update this switch, you get a compiler warning. The compiler is tracking this for you.

Compare that to the abstract record approach, where you need:

```csharp
string description = pet switch
{
    Cat c => $"{c.Name} is a cat",
    Dog d => $"{d.Name} is a good dog",
    Bird b => $"{b.Name} can fly",
    _ => throw new InvalidOperationException("Unknown pet type")
};
```

That `_ =>` is doing nothing except hiding the compiler's inability to verify exhaustiveness. With union types, you can delete it.

## Union Matching: Transparent Unwrapping

One thing that catches people off guard: patterns match against the *value inside* the union, not the union struct itself. This is "transparent unwrapping."

So when you write `Cat c =>` in a switch over a `Pet`, the compiler is looking at `pet.Value` and checking if it's a `Cat`. That's what makes the matching feel natural — you're thinking about the cases, not the container.

The exceptions are `var` and `_` patterns. Those apply to the union struct itself, not the inner value:

```csharp
// 'p' is Pet, not the inner type
Pet p = pet switch
{
    var p => p
};

// This catches when Value is null
Pet? p2 = maybePet switch
{
    null => null,
    Cat c => c,
    Dog d => d,
    Bird b => b
};
```

The compiler tracks null state through union matching. If a case type is a reference type, the compiler knows whether `Value` can be null after a successful match.

## Real-World Use Cases

The most obvious win is result types. You've probably seen this pattern:

```csharp
// Before: third-party library or lots of boilerplate
public OneOf<Product, NotFound, ValidationError> GetProduct(int id) { ... }
```

With union types:

```csharp
public union ProductResult(Product, NotFound, ValidationError);

public ProductResult GetProduct(int id)
{
    if (id <= 0) return new ValidationError("Invalid ID");
    var product = _repository.Find(id);
    if (product is null) return new NotFound();
    return product;
}
```

Then at the call site:

```csharp
var message = GetProduct(42) switch
{
    Product p => $"Found: {p.Name}",
    NotFound => "Not found",
    ValidationError e => $"Invalid: {e.Message}"
};
```

No `OneOf` dependency. No abstract base class. Just the types you care about, with compile-time exhaustiveness checking.

Command/message dispatching is another strong fit:

```csharp
public union AppCommand(CreateUser, UpdateUser, DeleteUser, SendEmail);

void Handle(AppCommand command)
{
    switch (command)
    {
        case CreateUser cmd: HandleCreate(cmd); break;
        case UpdateUser cmd: HandleUpdate(cmd); break;
        case DeleteUser cmd: HandleDelete(cmd); break;
        case SendEmail cmd: HandleEmail(cmd); break;
    }
}
```

Adding a new command type surfaces every switch that needs updating. That's exactly the compiler assistance you want.

## Custom Union Types

The `union` keyword is syntactic sugar. Under the hood, the compiler generates a struct with `[Union]` and `IUnion`. You can build the same thing by hand using the attribute and interface — useful when you need class semantics, custom storage, or want to adapt existing types.

There's also a non-boxing access pattern for value-type performance. The generated `object? Value` property boxes value types. If you're building a hot path and your case types are structs, you can expose typed accessors:

```csharp
[Union]
public struct NumberUnion : IUnion
{
    private readonly int _intValue;
    private readonly double _doubleValue;
    private readonly bool _hasInt;

    public NumberUnion(int value) { _intValue = value; _hasInt = true; }
    public NumberUnion(double value) { _doubleValue = value; _hasInt = false; }

    public object? Value => _hasInt ? _intValue : _doubleValue;

    public bool HasInt => _hasInt;
    public bool TryGetInt(out int value) { value = _intValue; return _hasInt; }
    public bool TryGetDouble(out double value) { value = _doubleValue; return !_hasInt; }
}
```

More code, but zero boxing. Worth it in tight loops or high-throughput scenarios.

## Union Types vs. Closed Hierarchies

C# 15 also introduces `closed` classes — a related but distinct feature. A `closed` class restricts which types can inherit from it, enabling exhaustive matching over class hierarchies:

```csharp
public closed abstract class Shape;
public class Circle(double Radius) : Shape;
public class Rectangle(double Width, double Height) : Shape;
```

When should you use each?

- **Union types**: when the cases are genuinely separate types with no inheritance relationship, or when you want value-type semantics. Great for result types, commands, messages.
- **Closed hierarchies**: when the cases share behaviour through inheritance, when you want polymorphic dispatch, or when the types already exist in a hierarchy.

They complement each other. A union could contain types from a closed hierarchy. A closed hierarchy could have a union as a property.

## Type Unions vs. Discriminated Unions

There's a terminology distinction worth knowing. C#'s union types are *type unions* — the union holds one of a set of existing types. F# and Haskell have *discriminated/tagged unions*, where each case can carry different data and is identified by a tag.

C# can simulate the tagged approach by using distinct types for each case:

```csharp
// These are fresh types, not reused ones
public record Ok<T>(T Value);
public record Err(string Message);
public union Result<T>(Ok<T>, Err);
```

Now `Ok<T>` and `Err` are purpose-built case types — effectively the same as DU cases in F#. You get exhaustive matching, distinct shapes per case, and clear intent at the call site.

If you reuse types (e.g., `union StringOrInt(string, int)`), you lose that — a `string` is a `string`, not tagged with anything meaningful. Worth keeping in mind when you're designing your unions.

## Current Status

Union types shipped in .NET 11 Preview 2. In early previews, you need to declare `UnionAttribute` and `IUnion` manually in your project — they're not yet in the runtime library. Later previews include them. The `union` keyword works from the start.

Some features aren't implemented yet in preview builds — union member providers and a few other tooling integrations are still in progress. The core functionality works, though. You can try it today with the .NET 11 Preview SDK or Visual Studio 2026 Insiders.

The champion issue consolidating all the union type proposals is [dotnet/csharplang#9662](https://github.com/dotnet/csharplang/issues/9662) if you want to follow development. The language reference and formal spec are on Microsoft Docs.

## Wrapping Up

Union types fill a gap that's been papered over with workarounds for a long time. The `union` keyword is simple, the implicit conversions feel natural, and the exhaustive matching is exactly the compiler assistance that makes these worthwhile.

The main thing to watch: patterns match the value *inside* the union, not the union itself. Once that clicks, everything else follows naturally.

Worth trying in your next side project before it ships. The ergonomics are good, and there's a decent chance your result type abstraction gets a lot simpler.
