---
title: Repository Pattern - Abstracting Data Access
date: 2026-03-04
tags: csharp, design-patterns, repository-pattern, architecture, testing
image: repository-pattern-abstracting-data-access.png
---

We've been working through a design patterns series. We've covered [SOLID principles](solid-principles-foundation-of-good-design.html), [composition over inheritance](composition-over-inheritance-building-flexible-systems.html), the [Strategy Pattern](strategy-pattern-swapping-algorithms-at-runtime.html), and most recently the [Factory Pattern](factory-pattern-hiding-object-creation.html). Today we're tackling something every application developer hits: how to talk to a database without letting that detail infect your entire codebase.

The Repository Pattern gives your business logic a clean interface to work with data. Instead of scattering SQL queries or EF Core calls throughout your application, you put all that data access logic in one place — the repository. Your business logic says "give me a customer", and it doesn't care whether that customer comes from SQL Server, a JSON file, or an in-memory list.

## The Problem: Data Access Everywhere

Here's what a typical service looks like without the Repository Pattern:

```csharp
public class OrderService
{
    private readonly string _connectionString;

    public OrderService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<decimal> CalculateCustomerTotalAsync(int customerId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var orders = await connection.QueryAsync<Order>(
            "SELECT * FROM Orders WHERE CustomerId = @customerId AND Status = 'Completed'",
            new { customerId });

        return orders.Sum(o => o.Total);
    }

    public async Task PlaceOrderAsync(Order order)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT INTO Orders (CustomerId, Total, Status, CreatedAt) VALUES (@CustomerId, @Total, @Status, @CreatedAt)",
            order);
    }
}
```

This works, but it has several problems:

- **SQL is mixed with business logic** — The `OrderService` knows about table names, column names, and SQL syntax. When your schema changes, you're hunting through business logic to find SQL strings.
- **Hard to test** — You need a real database to test `CalculateCustomerTotalAsync`. Setting up a test database, seeding it with data, and cleaning up afterwards is painful.
- **Hard to switch data sources** — If you want to move from Dapper to EF Core, or add caching, you're modifying business logic classes.
- **Duplication** — `new SqlConnection(_connectionString)` appears in every method. That same pattern will be duplicated in dozens of service classes.

## The Repository Interface

The fix is to define a contract for data access and hide the implementation behind it:

```csharp
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int id);
    Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId);
    Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId);
    Task AddAsync(Order order);
    Task UpdateAsync(Order order);
    Task DeleteAsync(int id);
}
```

Now `OrderService` depends on that interface, not on any concrete data access technology:

```csharp
public class OrderService
{
    private readonly IOrderRepository _orderRepository;

    public OrderService(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<decimal> CalculateCustomerTotalAsync(int customerId)
    {
        var orders = await _orderRepository.GetCompletedByCustomerIdAsync(customerId);
        return orders.Sum(o => o.Total);
    }

    public async Task PlaceOrderAsync(Order order)
    {
        await _orderRepository.AddAsync(order);
    }
}
```

The business logic now reads like what it actually does: get completed orders, sum the totals. The SQL is someone else's problem.

## The Concrete Repository

You still need an implementation that actually talks to the database. That's your concrete repository:

```csharp
public class SqlOrderRepository : IOrderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlOrderRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Order>(
            "SELECT * FROM Orders WHERE Id = @id", new { id });
    }

    public async Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Order>(
            "SELECT * FROM Orders WHERE CustomerId = @customerId",
            new { customerId });
    }

    public async Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Order>(
            "SELECT * FROM Orders WHERE CustomerId = @customerId AND Status = 'Completed'",
            new { customerId });
    }

    public async Task AddAsync(Order order)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "INSERT INTO Orders (CustomerId, Total, Status, CreatedAt) VALUES (@CustomerId, @Total, @Status, @CreatedAt)",
            order);
    }

    public async Task UpdateAsync(Order order)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE Orders SET Total = @Total, Status = @Status WHERE Id = @Id",
            order);
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "DELETE FROM Orders WHERE Id = @id", new { id });
    }
}
```

Notice that `SqlOrderRepository` takes an `IDbConnectionFactory` — not a raw connection string. That's the [Factory Pattern](factory-pattern-hiding-object-creation.html) from last time, working alongside the Repository Pattern. The factory handles creating connections; the repository handles data operations. Each has one job.

## Testing with an In-Memory Repository

Here's where the Repository Pattern really earns its keep. You can write a fake implementation that stores data in memory, and use it in all your unit tests:

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

    public Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId)
    {
        var orders = _orders.Where(o => o.CustomerId == customerId);
        return Task.FromResult(orders);
    }

    public Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId)
    {
        var orders = _orders.Where(o => o.CustomerId == customerId && o.Status == "Completed");
        return Task.FromResult(orders);
    }

    public Task AddAsync(Order order)
    {
        order.Id = _nextId++;
        _orders.Add(order);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order)
    {
        var existing = _orders.FirstOrDefault(o => o.Id == order.Id);
        if (existing != null)
        {
            existing.Total = order.Total;
            existing.Status = order.Status;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        _orders.RemoveAll(o => o.Id == id);
        return Task.CompletedTask;
    }
}
```

Your tests are now fast, simple, and need no database:

```csharp
public class OrderServiceTests
{
    [Fact]
    public async Task CalculateCustomerTotal_ReturnsOnlyCompletedOrders()
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
}
```

No connection strings. No database setup. No cleanup. Just fast, readable tests.

## Repository Pattern with EF Core

If you're using Entity Framework Core, the implementation looks a little different — but the interface stays exactly the same:

```csharp
public class EfOrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public EfOrderRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        return await _context.Orders.FindAsync(id);
    }

    public async Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId)
    {
        return await _context.Orders
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId)
    {
        return await _context.Orders
            .Where(o => o.CustomerId == customerId && o.Status == "Completed")
            .ToListAsync();
    }

    public async Task AddAsync(Order order)
    {
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Order order)
    {
        _context.Orders.Update(order);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order != null)
        {
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();
        }
    }
}
```

`OrderService` doesn't need to change at all. You swap `SqlOrderRepository` for `EfOrderRepository` in your DI registration and everything just works.

```csharp
// Before (Dapper)
services.AddScoped<IOrderRepository, SqlOrderRepository>();

// After (EF Core) – OrderService is untouched
services.AddScoped<IOrderRepository, EfOrderRepository>();
```

That's the whole point. Data access technology is an implementation detail. Business logic shouldn't care.

## Generic Repositories

For applications with many entity types, you'll often see a generic base repository to avoid repeating common CRUD operations:

```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(int id);
}

public class EfRepository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public EfRepository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(int id) => await _dbSet.FindAsync(id);
    public async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();

    public async Task AddAsync(T entity)
    {
        _dbSet.Add(entity);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _dbSet.FindAsync(id);
        if (entity != null)
        {
            _dbSet.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }
}
```

Specific repositories extend the generic one and add domain-specific queries:

```csharp
public interface IOrderRepository : IRepository<Order>
{
    Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId);
}

public class EfOrderRepository : EfRepository<Order>, IOrderRepository
{
    public EfOrderRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<Order>> GetCompletedByCustomerIdAsync(int customerId)
    {
        return await _dbSet
            .Where(o => o.CustomerId == customerId && o.Status == "Completed")
            .ToListAsync();
    }
}
```

You get the standard CRUD operations for free and only write code for queries that are specific to your domain.

## The Drawbacks

It's worth being honest about the downsides, because they're real.

**Abstraction overhead** — For simple applications, repositories add a layer that doesn't pay for itself. If you're writing a small CRUD app with three tables and no business logic, skipping the repository and querying EF Core directly is fine.

**Leaky abstractions** — Database-specific features can be hard to hide cleanly. Transactions that span multiple repositories, database-level optimisations, or features like EF Core's `Include()` for loading related data sometimes push back against the abstraction.

**The generic repository trap** — A `IRepository<T>` with just `GetByIdAsync` and `GetAllAsync` often gets misused. Teams call `GetAllAsync()` and then filter in C# instead of filtering in the database. That's a performance disaster waiting to happen. If you're going to use a generic repository, be deliberate about which queries you expose.

**EF Core already is a repository** — The `DbContext` and `DbSet<T>` are themselves repositories. Wrapping them in another repository layer is sometimes wrapping an abstraction in another abstraction. Some teams use EF Core directly in their services and only add repositories when they genuinely need the flexibility to swap data sources.

Use the Repository Pattern when the benefits — testability, swappable data sources, centralised query logic — actually apply to your situation. Don't use it out of habit.

## When to Use the Repository Pattern

Use it when:

1. **Testability matters** — You want to test business logic without a real database. An in-memory repository is far easier to manage than test databases.
2. **You might switch data sources** — Moving from Dapper to EF Core, adding a cache layer, supporting multiple databases.
3. **You have complex query logic** — Centralising queries in a repository prevents them from being scattered and duplicated across your codebase.
4. **Your codebase has many developers** — A clear data access interface makes it harder to accidentally write database logic in the wrong layer.

Skip it when:

1. **The application is simple** — CRUD apps with minimal business logic don't need this indirection.
2. **You're not writing tests** — If testability isn't a goal, the main driver for repositories disappears.
3. **EF Core is already your abstraction** — If you're fully committed to EF Core and won't be swapping it out, adding a repository layer on top can be unnecessary indirection.

## Wrapping Up

The Repository Pattern is about keeping data access concerns in one place and away from your business logic. Your services say what they need; repositories figure out how to provide it.

We've now seen how the [Factory Pattern](factory-pattern-hiding-object-creation.html) and the Repository Pattern complement each other — factories create connections and objects, repositories use those connections to implement data access behind a clean interface. Both patterns serve the same goal: your business logic works with abstractions, not with implementation details.

Up next in the series: the **Decorator Pattern**. Where the Repository Pattern hides *where* data comes from, the Decorator Pattern lets you add behaviour to an object without modifying it. You'll see how you can wrap a repository with a caching decorator, adding caching transparently without touching your business logic or your original repository implementation.

If you're already thinking "I could add caching to my repository without changing anything except my DI registration" — that's exactly the Decorator Pattern. We'll build it out properly next time.
