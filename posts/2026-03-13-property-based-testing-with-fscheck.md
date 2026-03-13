---
title: Property-Based Testing with FsCheck
date: 2026-03-13
tags: csharp, dotnet, testing, property-based-testing
image: property-based-testing-with-fscheck.png
---

We've been building up a serious testing toolkit this week. We covered [unit testing with xUnit](unit-testing-in-csharp-with-xunit.html), [TDD](test-driven-development-in-csharp.html), [mocking with Moq](mocking-with-moq-in-csharp.html), and [integration testing in ASP.NET Core](integration-testing-in-aspnet-core.html). All of those rely on *example-based* testing — you pick a specific input, assert a specific output, and move on. That works well, but it has a blind spot: you only test the cases you thought of.

Property-based testing takes a different approach. Instead of writing examples, you describe *properties* your code should always hold, and the framework generates hundreds of random inputs to try to break them. It finds the cases you didn't think of.

## Example-Based vs Property-Based

Here's the difference in concrete terms.

An example-based test for a sort function might look like this:

```csharp
[Fact]
public void Sort_ReturnsElementsInAscendingOrder()
{
    var input = new[] { 3, 1, 4, 1, 5 };
    var result = MySort(input);
    Assert.Equal(new[] { 1, 1, 3, 4, 5 }, result);
}
```

That's fine. But you chose those specific numbers. You probably didn't test an empty array, a single element, all duplicates, negative numbers, or `int.MaxValue`. And you likely didn't notice that your sort has a bug with exactly two equal elements.

A property-based test instead says: *for any list of integers, the sorted result should have the same elements as the input, in non-decreasing order.* The framework generates hundreds of random lists and checks your assertion against every one.

## Installing FsCheck

FsCheck is the go-to property-based testing library for .NET. Despite the "Fs" prefix, it works perfectly with C#. There's also a `FsCheck.Xunit` package that integrates with xUnit's test runner.

```bash
dotnet add package FsCheck
dotnet add package FsCheck.Xunit
```

You'll add these to your test project alongside your existing xUnit packages.

## Your First Property

Let's start with a mathematical property: addition is commutative. `a + b` should always equal `b + a`, for any integers `a` and `b`.

```csharp
using FsCheck;
using FsCheck.Xunit;

public class MathProperties
{
    [Property]
    public bool Addition_IsCommutative(int a, int b)
    {
        return a + b == b + a;
    }
}
```

That's it. The `[Property]` attribute tells FsCheck to run this method 100 times with randomly generated integers. If it ever returns `false`, the test fails and FsCheck reports the inputs that caused the failure.

Notice the return type is `bool` — you return `true` to say the property holds, `false` to say it doesn't. You can also use `Prop.ForAll` for more complex assertions, which we'll get to shortly.

## Properties for Your Domain

Mathematical properties are illustrative, but the real value comes when you apply this to your own domain objects.

Suppose you have a `Money` type with an `Add` method. You can express properties about it:

```csharp
public class MoneyProperties
{
    [Property]
    public bool Add_IsCommutative(decimal a, decimal b)
    {
        var x = new Money(a);
        var y = new Money(b);
        return x.Add(y) == y.Add(x);
    }

    [Property]
    public bool Add_IsAssociative(decimal a, decimal b, decimal c)
    {
        var x = new Money(a);
        var y = new Money(b);
        var z = new Money(c);
        return x.Add(y).Add(z) == x.Add(y.Add(z));
    }

    [Property]
    public bool AddingZero_ReturnsOriginalValue(decimal a)
    {
        var x = new Money(a);
        var zero = new Money(0m);
        return x.Add(zero) == x;
    }
}
```

These properties encode actual mathematical laws your `Money` type should satisfy. If any of them fail, FsCheck has found a real bug — not just a case you forgot to write an example for.

## Custom Generators

FsCheck knows how to generate `int`, `string`, `bool`, `decimal`, and many other built-in types out of the box. For your own domain types, you need to tell it how.

Let's say you have a `Product` class and you want to generate valid products for your tests:

```csharp
public record Product(string Name, decimal Price, int StockCount);
```

You write a generator using `Arb.Generate` and `Gen`:

```csharp
public static class ProductGenerators
{
    public static Arbitrary<Product> Products()
    {
        var nameGen = Gen.Elements("Widget", "Gadget", "Doohickey", "Thingamajig");
        var priceGen = Gen.Choose(1, 10000).Select(p => p / 100m);
        var stockGen = Gen.Choose(0, 1000);

        var productGen = from name in nameGen
                         from price in priceGen
                         from stock in stockGen
                         select new Product(name, price, stock);

        return productGen.ToArbitrary();
    }
}
```

To make FsCheck use your generator automatically, register it in your test class:

```csharp
public class ProductProperties
{
    public ProductProperties()
    {
        Arb.Register<ProductGenerators>();
    }

    [Property]
    public bool Price_IsAlwaysPositive(Product product)
    {
        return product.Price > 0m;
    }

    [Property]
    public bool ApplyDiscount_NeverExceedsOriginalPrice(Product product, byte discountPercent)
    {
        var discounted = product.ApplyDiscount(discountPercent % 101); // 0-100%
        return discounted.Price <= product.Price;
    }
}
```

The `byte` type for `discountPercent` is a useful trick — FsCheck generates bytes in the range 0–255, and the modulo constrains it to a valid percentage range without needing a custom generator.

### Filtering with Conditions

Sometimes you want to restrict which generated values the property applies to. Use `Prop.ForAll` with a `.When()` precondition:

```csharp
[Property]
public Property Division_RoundTrips()
{
    return Prop.ForAll<double, double>((numerator, divisor) =>
    {
        var result = numerator / divisor;
        var roundTrip = result * divisor;
        return Math.Abs(roundTrip - numerator) < 0.001;
    }.When((_, divisor) => Math.Abs(divisor) > 0.001));
}
```

The `.When(condition)` clause tells FsCheck to skip cases where the condition doesn't hold. FsCheck will keep generating until it has 100 cases where the condition passes, so be careful not to make the condition too restrictive or your tests will run slowly.

## Shrinking

This is the feature that makes property-based testing genuinely useful rather than just academically interesting.

When FsCheck finds a failing input, it doesn't just report that random string of 847 characters that caused the failure. It *shrinks* the input — it tries smaller and simpler versions until it finds the minimal case that still fails.

Suppose your string processing function has a bug that only shows up for strings containing the letter "z" followed by a digit. FsCheck might generate `"hello z3 world"` as the initial failing case, then shrink it down to `"z3"` — the smallest input that demonstrates the bug.

Built-in types like `int`, `string`, and `List<T>` shrink automatically. For custom types, your `Arbitrary<T>` needs to provide a shrinker. The simplest approach is to lean on FsCheck's built-in shrinkers:

```csharp
public static Arbitrary<Product> Products()
{
    // ... generator setup from before ...

    return productGen.ToArbitrary(); // FsCheck uses default shrinking for the component types
}
```

If you need fine-grained control, you can provide an explicit shrinker function as the second argument to `Arb.From`:

```csharp
return Arb.From(productGen, product =>
    // Return smaller variants of this product to try
    new[]
    {
        product with { Name = "A" },
        product with { Price = 0.01m },
        product with { StockCount = 0 }
    }.ToList().AsEnumerable()
);
```

## Using [Property] vs [Fact]

The `[Property]` attribute is a drop-in replacement for `[Fact]` in xUnit. FsCheck hooks into the xUnit test runner, so your properties show up alongside your facts in test output, CI results, and test explorers.

You can also configure the number of test cases FsCheck generates per property. The default is 100, which is usually fine, but you can bump it up for critical code:

```csharp
[Property(MaxTest = 1000)]
public bool CriticalAlgorithm_AlwaysTerminates(int[] input)
{
    var result = MyAlgorithm(input);
    return result != null;
}
```

Or configure it globally in an `FsCheckConfig` class if you want a project-wide default.

## When to Use Property-Based Testing

Property-based testing isn't a replacement for example-based testing — it's a complement.

**Use properties when:**

- You're implementing something with well-defined mathematical laws (serialisation round-trips, codec symmetry, algebraic structures like commutativity or associativity)
- You're dealing with a large input space where edge cases are hard to anticipate
- You want to verify invariants that should hold across your entire domain
- You're building parsers, formatters, or transformations where correctness is hard to characterise with a handful of examples

**Stick with examples when:**

- The expected output is specific and well-known (`ParseDate("2026-03-13")` returns a particular `DateOnly`)
- You're documenting intended behaviour as executable specification
- The input space is small and you can enumerate the meaningful cases
- You're testing interaction and sequencing rather than transformation

In practice, I use a mix. Example-based tests document the happy path and the known edge cases. Property-based tests probe the space between those examples and find the cases I didn't think to name.

## Pulling It Together

Here's a realistic property test for a discount calculation system that combines custom generators, preconditions, and multiple assertions:

```csharp
public class DiscountEngineProperties
{
    public DiscountEngineProperties()
    {
        Arb.Register<ProductGenerators>();
    }

    [Property]
    public Property BulkDiscount_NeverMakesOrderMoreExpensive()
    {
        return Prop.ForAll<Product[], int>((products, quantity) =>
        {
            var originalTotal = products.Sum(p => p.Price) * quantity;
            var discountedTotal = DiscountEngine.ApplyBulkDiscount(products, quantity);

            return (discountedTotal <= originalTotal)
                .Label($"Original: {originalTotal}, Discounted: {discountedTotal}");
        }.When((products, quantity) =>
            products.Length > 0 && quantity >= DiscountEngine.BulkThreshold));
    }

    [Property]
    public Property StackingDiscounts_AreCommutative()
    {
        return Prop.ForAll<Product, byte, byte>((product, discount1, discount2) =>
        {
            var d1 = discount1 % 51; // 0-50%
            var d2 = discount2 % 51; // 0-50%

            var applyFirstThenSecond = product.ApplyDiscount(d1).ApplyDiscount(d2).Price;
            var applySecondThenFirst = product.ApplyDiscount(d2).ApplyDiscount(d1).Price;

            return Math.Abs(applyFirstThenSecond - applySecondThenFirst) < 0.001m;
        });
    }
}
```

The `.Label()` call is worth noting — when a property fails, FsCheck includes the label in the error output. That's invaluable when you're staring at a shrunk failing case and trying to understand what went wrong.

## Wrapping Up the Testing Week

That's a complete testing toolkit. Unit tests for logic, TDD for design feedback, mocking for isolation, integration tests for the full stack, and now property-based tests to probe the input space you didn't think to cover.

FsCheck is particularly good at finding the kind of bug that hides in a corner case: the string that's almost valid, the number that's just slightly out of range, the combination of inputs that your example tests never happened to hit. Once you've had FsCheck find a real bug in production code — and it will — you'll start writing properties as a matter of course.

Add it to your existing test project, write a few properties for your core domain types, and see what it finds. The investment is low and the payoff is the kind of confidence that comes from knowing you've tested more than just the cases you could name.
