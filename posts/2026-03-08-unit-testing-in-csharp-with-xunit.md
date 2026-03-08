---
title: Unit Testing in C# with xUnit
date: 2026-03-08
tags: csharp, dotnet, testing, xunit
image: unit-testing-in-csharp-with-xunit.png
---

Throughout this design patterns series, testing has come up again and again. The [Repository Pattern post](repository-pattern-abstracting-data-access.html) showed an `InMemoryOrderRepository` and used it in a quick test example. The [Dependency Injection post](dependency-injection-loosely-coupling-components.html) explained why injecting interfaces — instead of creating concretions directly — is what makes testing possible at all. We kept saying "and this makes it easy to test." Today we actually do the testing.

This post covers unit testing in C# using xUnit. We'll start with the basics and build up to testing with fakes and mocks, using the `IOrderRepository` pattern from the series as our example.

## Setting Up xUnit

If you're starting a new project, add an xUnit test project with:

```bash
dotnet new xunit -n MyProject.Tests
dotnet add MyProject.Tests reference MyProject
```

That's really all the setup you need. xUnit is included in the default template, and the test runner integrates with `dotnet test`, Visual Studio, and Rider without any extra configuration.

## Your First Test

xUnit tests are just methods marked with `[Fact]`. There's no test class base type to inherit, no `[TestClass]` attribute — just a plain C# class with attributed methods.

```csharp
public class CalculatorTests
{
    [Fact]
    public void Add_TwoPositiveNumbers_ReturnsSum()
    {
        // Arrange
        var calculator = new Calculator();

        // Act
        var result = calculator.Add(2, 3);

        // Assert
        Assert.Equal(5, result);
    }
}
```

The **Arrange/Act/Assert** pattern is the standard structure for unit tests. Arrange sets up the objects and data you need. Act calls the code under test. Assert verifies the result. Keeping these three sections distinct makes tests easy to read and diagnose — when a test fails, you can see immediately *what* was called and *what* was expected.

Method naming follows a `MethodName_Condition_ExpectedResult` convention. It's verbose, but it pays off: a failing test named `CalculateCustomerTotal_WithNoPurchases_ReturnsZero` tells you far more than `TestCalculation2`.

## Parameterised Tests with Theory

`[Fact]` tests a single scenario. `[Theory]` tests the same logic with multiple inputs:

```csharp
public class CalculatorTests
{
    [Theory]
    [InlineData(2, 3, 5)]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(100, -50, 50)]
    public void Add_VariousInputs_ReturnsCorrectSum(int a, int b, int expected)
    {
        var calculator = new Calculator();
        var result = calculator.Add(a, b);
        Assert.Equal(expected, result);
    }
}
```

Each `[InlineData]` row becomes a separate test run. If the `(-1, 1, 0)` case fails, xUnit reports exactly which row failed — you don't have to guess. This is a much cleaner alternative to writing four separate test methods that all do the same thing.

## Testing with Fakes

Here's where the design patterns series really pays off. In the [Repository Pattern post](repository-pattern-abstracting-data-access.html), I mentioned testing against a fake in-memory implementation. Let's build that out properly.

Our `OrderService` depends on `IOrderRepository`:

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<decimal> CalculateCustomerTotalAsync(int customerId)
    {
        var orders = await _repository.GetCompletedByCustomerIdAsync(customerId);
        return orders.Sum(o => o.Total);
    }

    public async Task PlaceOrderAsync(Order order)
    {
        if (order.Total <= 0)
            throw new ArgumentException("Order total must be positive.", nameof(order));

        await _repository.AddAsync(order);
    }
}
```

Because `OrderService` takes an `IOrderRepository`, we can pass in a fake for tests:

```csharp
public class InMemoryOrderRepository : IOrderRepository
{
    private readonly List<Order> _orders = new();
    private int _nextId = 1;

    public Task<Order?> GetByIdAsync(int id)
    {
        var order = _orders.FirstOrDefault(o => o.Id == id);
        return Task.FromResult(order);
    }

    public Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId)
    {
        IEnumerable<Order> orders = _orders
            .Where(o => o.CustomerId == customerId && o.Status == "Completed");
        return Task.FromResult(orders);
    }

    public Task AddAsync(Order order)
    {
        order.Id = _nextId++;
        _orders.Add(order);
        return Task.CompletedTask;
    }
}
```

Now the tests write themselves:

```csharp
public class OrderServiceTests
{
    [Fact]
    public async Task CalculateCustomerTotal_OnlyCountsCompletedOrders()
    {
        // Arrange
        var repository = new InMemoryOrderRepository();
        await repository.AddAsync(new Order { CustomerId = 1, Total = 50m, Status = "Completed" });
        await repository.AddAsync(new Order { CustomerId = 1, Total = 30m, Status = "Pending" });
        await repository.AddAsync(new Order { CustomerId = 1, Total = 20m, Status = "Completed" });

        var service = new OrderService(repository);

        // Act
        var total = await service.CalculateCustomerTotalAsync(1);

        // Assert
        Assert.Equal(70m, total); // 50 + 20, not the pending 30
    }

    [Fact]
    public async Task PlaceOrder_WithZeroTotal_ThrowsArgumentException()
    {
        // Arrange
        var repository = new InMemoryOrderRepository();
        var service = new OrderService(repository);
        var order = new Order { CustomerId = 1, Total = 0m };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.PlaceOrderAsync(order));
    }

    [Fact]
    public async Task PlaceOrder_ValidOrder_AddsToRepository()
    {
        // Arrange
        var repository = new InMemoryOrderRepository();
        var service = new OrderService(repository);
        var order = new Order { CustomerId = 1, Total = 99m, Status = "Completed" };

        // Act
        await service.PlaceOrderAsync(order);

        // Assert
        var retrieved = await repository.GetByIdAsync(order.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(99m, retrieved.Total);
    }
}
```

No connection strings. No database setup. No cleanup. The tests run in milliseconds. This is exactly the payoff that was promised when we introduced the Repository Pattern — and it only works because `OrderService` depends on an interface, not a concrete class.

## Testing with Mocks Using NSubstitute

Fakes are great when you need a full working implementation. But sometimes you want something lighter — you just want to verify that a method was called, or control what a dependency returns, without writing a whole fake class. That's where mocking libraries come in.

The two most popular options for .NET are **Moq** and **NSubstitute**. Both do the same job. I'll use NSubstitute here because its syntax reads more like plain C#. Moq uses lambda-based setup that's more explicit but more verbose — pick whichever style your team prefers.

Add NSubstitute to your test project:

```bash
dotnet add package NSubstitute
```

Here's the same `OrderService` test, rewritten with a mock:

```csharp
using NSubstitute;

public class OrderServiceMockTests
{
    [Fact]
    public async Task CalculateCustomerTotal_CallsRepositoryWithCorrectCustomerId()
    {
        // Arrange
        var repository = Substitute.For<IOrderRepository>();
        repository
            .GetCompletedByCustomerIdAsync(42)
            .Returns(new List<Order>
            {
                new Order { CustomerId = 42, Total = 100m, Status = "Completed" },
                new Order { CustomerId = 42, Total = 25m, Status = "Completed" }
            });

        var service = new OrderService(repository);

        // Act
        var total = await service.CalculateCustomerTotalAsync(42);

        // Assert
        Assert.Equal(125m, total);
        await repository.Received(1).GetCompletedByCustomerIdAsync(42);
    }

    [Fact]
    public async Task PlaceOrder_ValidOrder_CallsAddAsync()
    {
        // Arrange
        var repository = Substitute.For<IOrderRepository>();
        var service = new OrderService(repository);
        var order = new Order { CustomerId = 1, Total = 50m };

        // Act
        await service.PlaceOrderAsync(order);

        // Assert
        await repository.Received(1).AddAsync(order);
    }
}
```

`Substitute.For<IOrderRepository>()` creates an auto-generated fake that implements the interface. `.Returns(...)` controls what a method returns. `Received(1)` verifies the method was called exactly once with that argument.

### Fakes vs Mocks: When to Use Each

The distinction matters more than it seems:

- **Fakes** are real implementations that behave correctly but use lightweight infrastructure (memory instead of a database). Use them when you want realistic behaviour — tests that verify the system works end-to-end through several collaborating objects.
- **Mocks** (or substitutes) are controlled stand-ins that let you specify return values and verify calls. Use them when you want to test one class in isolation and don't care about the collaborator's internal behaviour.

For `OrderService` tests, either approach works. I lean towards fakes for the happy-path tests and mocks for verifying side-effects — like confirming that a notification was sent, or that the repository was called the right number of times.

## Testing Async Code

You'll have noticed all the examples above use `async Task` test methods. xUnit handles async tests natively — just make the test method `async Task` and `await` whatever you need to. Don't make tests `async void`; xUnit can't catch exceptions from void async methods and your tests will silently pass even when they throw.

```csharp
[Fact]
public async Task GetOrder_WhenNotFound_ReturnsNull()
{
    var repository = Substitute.For<IOrderRepository>();
    repository.GetByIdAsync(999).Returns((Order?)null);

    var service = new OrderService(repository);
    var result = await repository.GetByIdAsync(999);

    Assert.Null(result);
}
```

## Shared Setup with the Constructor

xUnit creates a new instance of your test class for every test method. That means the constructor is your setup method — and there's no need for a `[SetUp]` attribute like in NUnit. If several tests need the same objects, initialise them in the constructor:

```csharp
public class OrderServiceTests
{
    private readonly InMemoryOrderRepository _repository;
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _repository = new InMemoryOrderRepository();
        _service = new OrderService(_repository);
    }

    [Fact]
    public async Task CalculateCustomerTotal_NoOrders_ReturnsZero()
    {
        var total = await _service.CalculateCustomerTotalAsync(1);
        Assert.Equal(0m, total);
    }

    [Fact]
    public async Task CalculateCustomerTotal_OnlyCountsCompletedOrders()
    {
        await _repository.AddAsync(new Order { CustomerId = 1, Total = 50m, Status = "Completed" });
        await _repository.AddAsync(new Order { CustomerId = 1, Total = 30m, Status = "Pending" });

        var total = await _service.CalculateCustomerTotalAsync(1);

        Assert.Equal(50m, total);
    }
}
```

Because xUnit creates a fresh instance per test, there's no shared state between test methods. Each test starts with a clean `_repository`. No risk of one test's data leaking into another's.

## Putting It All Together

Unit testing in C# isn't complicated once you have the right foundation. You need:

1. **Interfaces on your dependencies** — so you can swap real implementations for fakes or mocks in tests
2. **Constructor injection** — so tests can pass in those fakes without fighting the production wiring
3. **xUnit's `[Fact]` and `[Theory]`** — for single-scenario and parameterised tests
4. **Arrange/Act/Assert** — to keep each test clear and focused
5. **A mocking library** (NSubstitute or Moq) — for cases where you want call verification or a quick controlled stand-in

The design patterns series was really about this all along. The Repository Pattern gives you a seam for fakes. Dependency Injection gives you the ability to pass fakes in. The SOLID principles give you the small, focused classes that are easy to test in the first place.

If you've been following along from the [SOLID principles post](solid-principles-foundation-of-good-design.html) to here, you now have a complete toolkit: design your code around abstractions, inject dependencies, and test them in isolation. That's the foundation of maintainable .NET development.
