---
title: Dapper: Lightweight SQL in .NET
date: 2026-04-22
tags: dotnet, csharp, dapper, tutorial
image: dapper-lightweight-sql-in-dotnet.png
---

Entity Framework Core is great for a lot of scenarios, but sometimes you just want to write SQL and get results back without the overhead. That's where Dapper comes in. It's a micro-ORM — essentially a thin layer over ADO.NET that maps query results to C# objects. No migrations, no change tracking, no query translation. Just SQL.

## Getting Started

Add Dapper to your project:

```bash
dotnet add package Dapper
```

Dapper extends `IDbConnection`, so it works with any ADO.NET provider. For SQL Server, you'll also need:

```bash
dotnet add package Microsoft.Data.SqlClient
```

The simplest possible query looks like this:

```csharp
using var connection = new SqlConnection(connectionString);

var products = await connection.QueryAsync<Product>(
    "SELECT Id, Name, Price FROM Products");
```

That's it. Dapper opens the connection if it's closed, executes the query, and maps each row to a `Product` object by matching column names to properties. No configuration needed.

## Parameterized Queries

Always use parameters rather than string interpolation — this prevents SQL injection and lets the database reuse execution plans:

```csharp
var product = await connection.QuerySingleOrDefaultAsync<Product>(
    "SELECT Id, Name, Price FROM Products WHERE Id = @Id",
    new { Id = productId });
```

The anonymous object `new { Id = productId }` maps naturally to the `@Id` parameter. You can use any object with matching properties — anonymous objects, records, or regular classes all work.

For multiple parameters:

```csharp
var products = await connection.QueryAsync<Product>(
    """
    SELECT Id, Name, Price
    FROM Products
    WHERE CategoryId = @CategoryId
      AND Price <= @MaxPrice
    ORDER BY Name
    """,
    new { CategoryId = categoryId, MaxPrice = maxPrice });
```

## Inserting and Updating

Dapper doesn't know about your schema, so inserts and updates are just parameterized SQL:

```csharp
await connection.ExecuteAsync(
    "INSERT INTO Products (Name, Price, CategoryId) VALUES (@Name, @Price, @CategoryId)",
    new { Name = product.Name, Price = product.Price, CategoryId = product.CategoryId });
```

Or pass the object directly if the properties match the parameter names:

```csharp
await connection.ExecuteAsync(
    "INSERT INTO Products (Name, Price, CategoryId) VALUES (@Name, @Price, @CategoryId)",
    product);
```

For updates:

```csharp
var rowsAffected = await connection.ExecuteAsync(
    "UPDATE Products SET Price = @Price WHERE Id = @Id",
    new { Price = newPrice, Id = productId });
```

`ExecuteAsync` returns the number of rows affected, which is useful for validating that the update actually matched a row.

## Querying Multiple Types

Dapper handles joins cleanly with `QueryAsync` multi-mapping. Say you want to load orders with their associated customer in one query:

```csharp
var orders = await connection.QueryAsync<Order, Customer, Order>(
    """
    SELECT o.Id, o.Total, o.OrderDate,
           c.Id, c.Name, c.Email
    FROM Orders o
    INNER JOIN Customers c ON c.Id = o.CustomerId
    """,
    (order, customer) =>
    {
        order.Customer = customer;
        return order;
    },
    splitOn: "Id");
```

The `splitOn` parameter tells Dapper where to split the result set — it splits at every column named `Id` by default. When your join columns have different names, specify them explicitly: `splitOn: "CustomerId"`.

## Multiple Result Sets

Sometimes you want to load several related datasets in a single round-trip. `QueryMultipleAsync` handles this:

```csharp
using var multi = await connection.QueryMultipleAsync(
    """
    SELECT * FROM Products WHERE CategoryId = @CategoryId;
    SELECT * FROM Categories WHERE Id = @CategoryId;
    """,
    new { CategoryId = categoryId });

var products = await multi.ReadAsync<Product>();
var category = await multi.ReadSingleAsync<Category>();
```

Each call to `Read` or `ReadSingle` consumes the next result set in order. This is a clean way to avoid multiple round-trips when you genuinely need several queries at once.

## Transactions

Dapper works with ADO.NET transactions directly:

```csharp
using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

using var transaction = connection.BeginTransaction();

try
{
    await connection.ExecuteAsync(
        "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
        new { CustomerId = order.CustomerId, Total = order.Total },
        transaction: transaction);

    await connection.ExecuteAsync(
        "UPDATE Inventory SET Quantity = Quantity - 1 WHERE ProductId = @ProductId",
        new { ProductId = order.ProductId },
        transaction: transaction);

    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

Pass the transaction as a parameter to each Dapper call and it'll enlist in the transaction automatically.

## Stored Procedures

Calling stored procedures is straightforward:

```csharp
var results = await connection.QueryAsync<OrderSummary>(
    "usp_GetOrdersByCustomer",
    new { CustomerId = customerId },
    commandType: CommandType.StoredProcedure);
```

For procedures with output parameters, use `DynamicParameters`:

```csharp
var parameters = new DynamicParameters();
parameters.Add("@CustomerId", customerId);
parameters.Add("@OrderCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

await connection.ExecuteAsync(
    "usp_GetCustomerOrderCount",
    parameters,
    commandType: CommandType.StoredProcedure);

var orderCount = parameters.Get<int>("@OrderCount");
```

## Dapper vs EF Core

Dapper and EF Core aren't mutually exclusive — many applications use both. The rough guide:

**Reach for Dapper when:**
- You're writing complex queries with CTEs, window functions, or specific join patterns that are awkward to express in LINQ
- You're working with legacy databases that don't map cleanly to entities
- You need maximum control over the SQL being executed
- You're building a read-heavy application and want every query to be exactly what you write

**Reach for EF Core when:**
- You want migrations to manage your schema
- You're doing CRUD operations on a well-modelled entity graph
- You want change tracking to handle updates automatically
- Your team is more comfortable with LINQ than SQL

For reporting, analytics, and read models in CQRS architectures, Dapper is a natural fit. The EF Core change tracker adds overhead that's pointless for data you'll never update. For the write side — where you're modifying entities and want transactional consistency — EF Core's change tracking earns its keep.

## Wrapping Up

Dapper's appeal is its simplicity. There's almost nothing to learn beyond the handful of extension methods — `QueryAsync`, `QuerySingleOrDefaultAsync`, `ExecuteAsync`, and a few variants. You write SQL, Dapper maps the results. That's the whole model.

If you're already comfortable with SQL, you'll be productive immediately. And when performance matters, you know exactly what query is hitting the database because you wrote it yourself.

The Dapper GitHub repo also has a companion library, Dapper.Contrib, if you want basic CRUD helpers without writing every `INSERT` and `UPDATE` by hand — though at that point you're getting closer to a full ORM, and EF Core might be the better choice anyway.
