---
title: Imposter: Fast Source-Generated Mocks
date: 2026-03-11
tags: csharp, dotnet, testing, mocking
image: imposter-source-generated-mocking.png
---

Yesterday we covered [Moq](mocking-with-moq-in-csharp.html) — the most popular mocking library in .NET. Today we're looking at [Imposter](https://github.com/themidnightgospel/Imposter), a newer library that takes a different approach: source generation.

Instead of creating mock objects at runtime using dynamic proxies, Imposter generates the mock classes at compile time. That changes the game when it comes to performance.

## What's Source Generation Got to Do With It?

Moq and NSubstitute create mock objects at runtime using `Castle.DynamicProxy` or similar techniques. It works, but it's slow to set up and uses more memory than you'd like.

Imposter uses Roslyn source generators. When you add the `[GenerateImposter]` attribute to your test project, the compiler generates the mock classes for you — as real C# code. No runtime magic, no reflection overhead.

The result? Imposter is roughly 10× faster than NSubstitute and up to 50× faster than Moq in common scenarios.

## Setup

Add the NuGet package to your test project:

```bash
dotnet add package Imposter
```

Then tell Imposter which interfaces to generate mocks for. This goes at the assembly level in any file in your test project:

```csharp
[assembly: GenerateImposter(typeof(IInventoryRepository))]
```

The source generator kicks in during build and creates a concrete mock class. You're ready to go.

## Basic Usage

Let's use the same `IInventoryRepository` from the Moq post:

```csharp
public interface IInventoryRepository
{
    int GetStock(string productId);
    void DeductStock(string productId, int quantity);
}
```

Create the imposter, configure it, and inject the instance:

```csharp
// C# 14+ extension method syntax
var imposter = IInventoryRepository.Imposter();

// C# 9-13 constructor syntax
// var imposter = new IInventoryRepositoryImposter();

var service = new OrderService(imposter.Instance());
```

`imposter.Instance()` returns the object that implements `IInventoryRepository` — equivalent to Moq's `mock.Object`.

## Setting Up Return Values

The API is clean and strongly typed:

```csharp
imposter.GetStock(Arg<string>.Any()).Returns(100);
```

`Arg<string>.Any()` matches any string argument — the Imposter equivalent of Moq's `It.IsAny<string>()`. For a specific value, pass it directly:

```csharp
imposter.GetStock("WIDGET-01").Returns(100);
```

Here's a full test:

```csharp
[Fact]
public void PlaceOrder_WhenStockAvailable_ReturnsSuccess()
{
    var imposter = IInventoryRepository.Imposter();
    imposter.GetStock(Arg<string>.Any()).Returns(100);

    var service = new OrderService(imposter.Instance());
    var result = service.PlaceOrder("WIDGET-01", quantity: 10);

    Assert.Equal(OrderResult.Success, result);
}
```

Notice you call the method directly on the imposter — no lambda expressions needed. Strongly typed, and the compiler catches mistakes at build time.

## Chained Return Values

One nice feature: you can chain multiple return values for sequential calls.

```csharp
imposter.GetStock(Arg<string>.Any())
    .Returns(100)
    .Then()
    .Returns(0);
```

The first call returns `100`, the second returns `0`. This is useful when you need to simulate changing state across multiple calls — without any extra setup.

## Implicit vs Explicit Mode

Imposter gives you control over what happens when you call an unmocked method.

**Implicit mode** (the default) returns safe defaults — `0`, `null`, empty collections. Handy when you don't care about all the methods on an interface.

**Explicit mode** throws an exception if any method is called without a setup:

```csharp
var imposter = IInventoryRepository.Imposter(ImposterMode.Explicit);
```

Explicit mode is great for strict tests where you want to ensure only the expected interactions happen. If your code unexpectedly calls a dependency, you'll know immediately. Moq's equivalent is `MockBehavior.Strict`.

## Mocking Classes Too

Imposter isn't limited to interfaces. It can mock non-sealed classes and their protected members:

```csharp
[assembly: GenerateImposter(typeof(BaseOrderProcessor))]
```

This is handy when you're working with legacy code or a design that uses abstract base classes rather than interfaces.

## Comparing Imposter and Moq

Both libraries solve the same problem, but differently:

| Feature | Moq | Imposter |
|---|---|---|
| Approach | Runtime proxy | Source generation |
| Performance | Baseline | ~50× faster |
| Setup syntax | Lambda-based | Method-call-based |
| Argument matching | `It.IsAny<T>()` | `Arg<T>.Any()` |
| Chained returns | `SetupSequence` | `.Then().Returns()` |
| Strict mode | `MockBehavior.Strict` | `ImposterMode.Explicit` |
| Mocking classes | Yes | Yes |

The API feel is similar enough that switching isn't painful. Imposter trades the familiar lambda syntax for a more direct method-call style — and gains significant performance in return.

## When Should You Switch?

Moq is still great. It's been around for years, has a huge ecosystem, and most .NET developers know it. If your tests are already using Moq and everything works, there's no urgent reason to switch.

Imposter shines when:

- **Performance matters** — large test suites where mock setup time adds up
- **You want strict tests** — Explicit mode makes it easy to catch unexpected interactions
- **You like the style** — calling methods directly rather than lambdas reads naturally to some developers

It's also worth evaluating for greenfield projects where you don't have an existing Moq investment.

## Conclusion

Imposter is a well-designed mocking library that uses .NET source generation to deliver excellent performance without sacrificing API quality. The method-call style feels natural, chained returns are elegant, and Explicit mode makes strict testing easy.

If you're evaluating testing libraries for your next project — or frustrated with slow test suite startup times — give [Imposter](https://github.com/themidnightgospel/Imposter) a look.
