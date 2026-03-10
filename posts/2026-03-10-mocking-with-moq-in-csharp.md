---
title: Mocking with Moq in C#
date: 2026-03-10
tags: csharp, dotnet, testing, moq, mocking
image: mocking-with-moq-in-csharp.png
---

Over the past couple of days we've covered [unit testing with xUnit](unit-testing-in-csharp-with-xunit.html) and [Test-Driven Development](test-driven-development-in-csharp.html). By now you're writing tests and letting them drive your design. But there's a problem that keeps coming up: your code has dependencies, and those dependencies make testing painful.

Say your service calls a database, sends an email, or hits an external API. You don't want any of that happening in a unit test. You need test doubles — fake stand-ins that let you control what the dependency does.

## Hand-Rolled Fakes Get Old Fast

You can write fakes by hand. Implement the interface, return hardcoded values, maybe track whether a method was called. For a simple interface it's fine. For anything with a dozen methods, it becomes a maintenance nightmare.

That's where [Moq](https://github.com/devlooped/moq) comes in.

## What Is Moq?

Moq (pronounced "mock" or "moque") is the most popular mocking library for .NET. It lets you create mock objects from interfaces and abstract classes with minimal boilerplate — and provides a clean API for setting up behaviour and verifying interactions.

It uses lambda expressions and generics so your setup code is strongly typed and refactor-friendly. If you rename a method on your interface, the compiler will tell you your mock setup is broken.

## Setup

Add the NuGet package to your test project:

```bash
dotnet add package Moq
```

Then add the using directive:

```csharp
using Moq;
```

That's it. Let's see it in action.

## Basic Mocking

Suppose you're building an `OrderService` that depends on an `IInventoryRepository`. In a unit test, you don't want to hit a real database — you want to control exactly what the repository returns.

Here's the interface:

```csharp
public interface IInventoryRepository
{
    int GetStock(string productId);
    void DeductStock(string productId, int quantity);
}
```

And here's how you create a mock of it:

```csharp
var mockRepo = new Mock<IInventoryRepository>();
```

`mockRepo` is a `Mock<IInventoryRepository>`. The actual object you pass into your class under test is `mockRepo.Object`:

```csharp
var service = new OrderService(mockRepo.Object);
```

By default, a Moq mock returns safe defaults — `0` for integers, `null` for reference types, empty collections, and so on. That's often enough for a simple test. But usually you'll want to control the return values.

## Setting Up Return Values

Use `Setup()` to define what a method should return when it's called:

```csharp
mockRepo.Setup(r => r.GetStock("WIDGET-01")).Returns(100);
```

Now whenever `GetStock("WIDGET-01")` is called on the mock, it returns `100`. Here's a full test:

```csharp
[Fact]
public void PlaceOrder_WhenStockAvailable_ReturnsSuccess()
{
    var mockRepo = new Mock<IInventoryRepository>();
    mockRepo.Setup(r => r.GetStock("WIDGET-01")).Returns(100);

    var service = new OrderService(mockRepo.Object);
    var result = service.PlaceOrder("WIDGET-01", quantity: 10);

    Assert.Equal(OrderResult.Success, result);
}
```

Clean and readable. The test tells a clear story without any hand-rolled fake class.

### Async Methods

If your repository returns a `Task`, use `ReturnsAsync()`:

```csharp
mockRepo.Setup(r => r.GetStockAsync("WIDGET-01")).ReturnsAsync(100);
```

It works exactly the same way — Moq handles the task wrapping for you.

## Verifying Interactions

Sometimes the point of a test isn't what's *returned*, it's whether a method was *called*. Use `Verify()` for that:

```csharp
[Fact]
public void PlaceOrder_WhenSuccessful_DeductsStock()
{
    var mockRepo = new Mock<IInventoryRepository>();
    mockRepo.Setup(r => r.GetStock("WIDGET-01")).Returns(100);

    var service = new OrderService(mockRepo.Object);
    service.PlaceOrder("WIDGET-01", quantity: 10);

    mockRepo.Verify(r => r.DeductStock("WIDGET-01", 10), Times.Once);
}
```

`Times.Once` asserts the method was called exactly once. You can also use `Times.Never`, `Times.Exactly(n)`, `Times.AtLeastOnce`, and more.

If you want to verify *all* setups were called, use `VerifyAll()`:

```csharp
mockRepo.VerifyAll();
```

This is useful to catch cases where your code never called a dependency it should have.

## Argument Matching

Hard-coding argument values in `Setup()` only matches that exact call. When you want to match *any* argument of a type, use `It.IsAny<T>()`:

```csharp
mockRepo.Setup(r => r.GetStock(It.IsAny<string>())).Returns(50);
```

Now the mock returns `50` regardless of which product ID is passed in.

For more precise control, use `It.Is<T>()` with a predicate:

```csharp
mockRepo.Setup(r => r.GetStock(It.Is<string>(id => id.StartsWith("WIDGET"))))
        .Returns(100);
```

This only matches product IDs that start with `"WIDGET"`. Everything else returns the default.

Argument matchers also work in `Verify()`:

```csharp
mockRepo.Verify(r => r.DeductStock(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
```

## Throwing Exceptions

Need to test your error-handling path? Set up the mock to throw:

```csharp
[Fact]
public void PlaceOrder_WhenRepositoryThrows_ReturnsFailure()
{
    var mockRepo = new Mock<IInventoryRepository>();
    mockRepo.Setup(r => r.GetStock(It.IsAny<string>()))
            .Throws(new DatabaseException("Connection lost"));

    var service = new OrderService(mockRepo.Object);
    var result = service.PlaceOrder("WIDGET-01", quantity: 1);

    Assert.Equal(OrderResult.Failure, result);
}
```

This forces the unhappy path without you having to engineer a real database failure. It's one of the best things about mocking.

## Mocks vs Fakes — When to Use Each

Mocks aren't always the right tool. Here's a quick rule of thumb:

- **Use a mock** when you need to verify that an interaction happened — that a method was called, with specific arguments, a specific number of times.
- **Use a fake** (a real implementation with simplified behaviour) when you care about the *outcome* of the interaction, not whether it happened. An in-memory repository is a classic example.
- **Use a stub** (a simple hardcoded return value) when you just need the dependency to return something so your code doesn't blow up.

Moq can do all three, but if you find yourself writing complex mock setups to simulate real behaviour, that's often a sign a fake would be cleaner.

## Conclusion

Moq removes the friction from testing code with dependencies. You set up what you need, inject `mock.Object`, assert the outcome, and verify the interactions. No hand-rolled fake classes, no interface boilerplate.

Combined with the xUnit and TDD techniques from the last two posts, you've now got a solid testing toolkit. Write the test first, mock the dependencies, drive your design from the outside in. That's a powerful loop.

Tomorrow we'll look at [Imposter](https://github.com/themidnightgospel/Imposter) — another .NET testing library worth adding to your toolkit. Stay tuned.
