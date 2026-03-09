---
title: Test-Driven Development in C#
date: 2026-03-09
tags: csharp, dotnet, testing, tdd, xunit
image: test-driven-development-in-csharp.png
---

If you read [yesterday's post on unit testing with xUnit](unit-testing-in-csharp-with-xunit.html), you've already got the tools you need to write tests. Today we're going to flip the script — instead of writing code first and tests after, we'll write tests *first* and let them drive our design. That's Test-Driven Development, or TDD.

It sounds counterintuitive at first. How do you test code that doesn't exist yet? But once it clicks, you'll wonder how you ever built software any other way.

## What Is TDD?

TDD is a development technique built around one simple loop: **Red → Green → Refactor**.

1. **Red**: Write a test for behaviour that doesn't exist yet. It should fail — if it doesn't, something's wrong.
2. **Green**: Write the *minimum* amount of code to make the test pass. Don't gold-plate it.
3. **Refactor**: Clean up the code while keeping the tests green.

Repeat. That's it. The magic is in the discipline of staying in that loop.

The tests you write aren't just a safety net — they're a specification. You're forced to think about *what* you want before *how* you'll build it.

## The Red-Green-Refactor Loop in Action

Let's start with something tiny: a method that checks whether a number is even.

### Red — write the failing test

```csharp
public class MathHelperTests
{
    [Fact]
    public void IsEven_ReturnsTrueForEvenNumber()
    {
        var result = MathHelper.IsEven(4);
        Assert.True(result);
    }
}
```

This doesn't compile yet — `MathHelper` doesn't exist. That's fine. A compile error *is* a failing test in TDD terms.

### Green — make it pass

```csharp
public static class MathHelper
{
    public static bool IsEven(int number) => number % 2 == 0;
}
```

Now the test passes. We wrote just enough code — no more.

### Refactor

There's nothing to clean up here. The method is already clean. That's often the case for simple logic, but as complexity grows, the refactor step becomes increasingly valuable.

## A Practical TDD Example: PasswordValidator

Let's build something more realistic. We want a `PasswordValidator` class that enforces password rules. We don't know exactly how it'll look yet — TDD will shape it.

### Start with the simplest rule

A password must be at least 8 characters.

```csharp
public class PasswordValidatorTests
{
    [Fact]
    public void Validate_ReturnsFalse_WhenPasswordIsTooShort()
    {
        var validator = new PasswordValidator();
        var result = validator.Validate("abc");
        Assert.False(result.IsValid);
    }
}
```

Run it — red. Now make it green:

```csharp
public class ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public class PasswordValidator
{
    public ValidationResult Validate(string password)
    {
        if (password.Length < 8)
            return new ValidationResult { IsValid = false };

        return new ValidationResult { IsValid = true };
    }
}
```

Green. Next rule.

### Add a rule: must contain a digit

```csharp
[Fact]
public void Validate_ReturnsFalse_WhenPasswordHasNoDigit()
{
    var validator = new PasswordValidator();
    var result = validator.Validate("abcdefgh");
    Assert.False(result.IsValid);
}
```

Red. Update the implementation:

```csharp
public ValidationResult Validate(string password)
{
    var errors = new List<string>();

    if (password.Length < 8)
        errors.Add("Password must be at least 8 characters.");

    if (!password.Any(char.IsDigit))
        errors.Add("Password must contain at least one digit.");

    return new ValidationResult
    {
        IsValid = errors.Count == 0,
        Errors = errors
    };
}
```

Green. Both previous tests still pass too — that's the safety net working for you.

### Add a rule: must contain an uppercase letter

```csharp
[Fact]
public void Validate_ReturnsFalse_WhenPasswordHasNoUppercase()
{
    var validator = new PasswordValidator();
    var result = validator.Validate("abcdefg1");
    Assert.False(result.IsValid);
}

[Fact]
public void Validate_ReturnsTrue_WhenPasswordMeetsAllRules()
{
    var validator = new PasswordValidator();
    var result = validator.Validate("Abcdefg1");
    Assert.True(result.IsValid);
}
```

Red. Add the rule:

```csharp
if (!password.Any(char.IsUpper))
    errors.Add("Password must contain at least one uppercase letter.");
```

Green. Now we have a working `PasswordValidator` with four passing tests — and we built it entirely from the outside in. Notice we never had to guess what the public API should look like; the tests told us.

### Refactor — extract the rules

Now that all tests are green, let's clean things up. The `Validate` method will get messy as we add more rules. Let's extract them:

```csharp
public class PasswordValidator
{
    private static readonly IReadOnlyList<(Func<string, bool> Rule, string Error)> Rules =
    [
        (p => p.Length >= 8, "Password must be at least 8 characters."),
        (p => p.Any(char.IsDigit), "Password must contain at least one digit."),
        (p => p.Any(char.IsUpper), "Password must contain at least one uppercase letter."),
    ];

    public ValidationResult Validate(string password)
    {
        var errors = Rules
            .Where(r => !r.Rule(password))
            .Select(r => r.Error)
            .ToList();

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}
```

Run the tests. Still green. The refactor is complete — the code is now easier to extend (adding a new rule is one line) and all tests still document the expected behaviour.

## TDD with the Repository Pattern

If you've been following along with this blog's [design patterns series](design-patterns-series-composition-over-complexity.html), you already know `IOrderRepository`. Here's something interesting: TDD would have *driven* you to that interface.

When you write a test for a service that needs to load orders, you immediately hit a problem:

```csharp
[Fact]
public void GetTotal_ReturnsCorrectSum_ForAllOrders()
{
    // How do we inject test data here without a real database?
}
```

The test forces the question. You can't control what `OrderService` returns if it creates its own `SqlOrderRepository` internally. So you extract an interface, inject it, and swap in a fake in the test:

```csharp
public class FakeOrderRepository : IOrderRepository
{
    private readonly List<Order> _orders;
    public FakeOrderRepository(List<Order> orders) => _orders = orders;
    public Task<IEnumerable<Order>> GetAllAsync() => Task.FromResult(_orders.AsEnumerable());
}

[Fact]
public async Task GetTotal_ReturnsCorrectSum_ForAllOrders()
{
    var repo = new FakeOrderRepository(
    [
        new Order { Total = 10.00m },
        new Order { Total = 25.50m },
    ]);
    var service = new OrderService(repo);

    var total = await service.GetTotalAsync();

    Assert.Equal(35.50m, total);
}
```

TDD doesn't just test your code — it *designs* it. The pressure to make things testable pushes you toward loose coupling and good abstractions almost automatically.

## When to Use TDD

TDD is genuinely great for:

- **Business logic** — calculators, validators, rules engines. The logic is well-defined and easy to specify upfront.
- **Bug fixes** — write a failing test that reproduces the bug, then fix it. You'll never regress on it again.
- **Refactoring** — having tests green before you start a refactor means you know the moment you break something.
- **APIs you'll call often** — the test is the first consumer of your API. If it's awkward to test, it'll be awkward to use.

TDD is less suited for:

- **Exploratory code** — when you're not sure what you're building yet, write a spike first, throw it away, then TDD the real thing.
- **UI and infrastructure glue** — testing button clicks or HTTP wire formats at the unit level often produces fragile tests. Save integration/E2E tests for that.
- **Simple CRUD with no logic** — if your "service" is just calling a repo method and returning the result, you're probably testing the framework, not your code.

The rule of thumb: if you can clearly state what the code *should do* before writing it, TDD will serve you well.

## Conclusion

TDD is a habit, not a tool. Like most habits, it feels awkward at first and natural after a few weeks. The benefits compound over time: you end up with a test suite that documents your system's behaviour, a codebase that's easier to change, and a design that emerges from real usage rather than speculation.

Start small. Pick one class in your next feature and TDD it from scratch. You don't have to go all-in immediately — just see how it feels.

Key takeaways:

- **Red → Green → Refactor** is the loop. Stay in it.
- Write the *minimum* code to pass the test, then clean up in the refactor step.
- TDD drives good design by forcing you to think about the API before the implementation.
- Testable code tends to be well-structured code. The pressure is a feature, not a bug.
