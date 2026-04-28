---
title: Expression Trees in C#
date: 2026-04-28
tags: dotnet, csharp, linq, tutorial
image: expression-trees-in-csharp.png
---

Lambda expressions in C# do double duty. Most of the time they're just delegates — compiled functions you call directly. But when assigned to `Expression<TDelegate>`, they become data: a tree of objects representing the code's structure that you can inspect, transform, and compile at runtime. That's the foundation of how LINQ providers like EF Core turn C# predicates into SQL.

This post walks through what expression trees are, how to use them, and where they actually matter in practice.

## Delegates vs Expressions

The difference between the two is in the type you assign the lambda to:

```csharp
// Compiled delegate — this is executable code
Func<int, bool> isEven = x => x % 2 == 0;

// Expression tree — this is a data structure representing the code
Expression<Func<int, bool>> isEvenExpr = x => x % 2 == 0;
```

`isEven` is a method pointer. You call it with a value and get a result. `isEvenExpr` is a tree of `Expression` objects describing the lambda: a parameter `x`, a modulo operation, a comparison, and a constant. It's not executable until you compile it.

The compiler does the heavy lifting. When it sees a lambda assigned to `Expression<TDelegate>`, it generates code that builds the expression tree rather than emitting IL for the lambda body.

## Inspecting an Expression Tree

Expression trees are made up of `Expression` subclasses. You can walk the tree manually:

```csharp
Expression<Func<Order, bool>> predicate = o => o.Total > 100 && o.Status == "Shipped";

// The body of the lambda
var body = (BinaryExpression)predicate.Body; // &&

var left = (BinaryExpression)body.Left;   // o.Total > 100
var right = (BinaryExpression)body.Right; // o.Status == "Shipped"

Console.WriteLine(left.NodeType);  // GreaterThan
Console.WriteLine(right.NodeType); // Equal

var totalAccess = (MemberExpression)left.Left; // o.Total
Console.WriteLine(totalAccess.Member.Name);    // Total
```

Every node in the tree has a `NodeType` (an `ExpressionType` enum), and depending on the type, it exposes further children. `BinaryExpression` has `Left` and `Right`. `MemberExpression` has `Member` and `Expression`. `MethodCallExpression` has `Method` and `Arguments`.

## Compiling and Calling

Once you have an expression tree, you can compile it into a delegate:

```csharp
Expression<Func<int, int, int>> addExpr = (a, b) => a + b;

Func<int, int, int> add = addExpr.Compile();
int result = add(3, 4); // 7
```

Compilation is expensive — it involves IL generation at runtime. Cache the compiled delegate; don't recompile on every call.

## Building Expression Trees Programmatically

You can also construct trees using the static `Expression` factory methods, without writing a lambda. This is how query providers and dynamic query builders work:

```csharp
// Build: x => x.Price > minPrice
var parameter = Expression.Parameter(typeof(Product), "x");
var property = Expression.Property(parameter, nameof(Product.Price));
var constant = Expression.Constant(50.0m);
var comparison = Expression.GreaterThan(property, constant);

var lambda = Expression.Lambda<Func<Product, bool>>(comparison, parameter);

// Compile and use
var filter = lambda.Compile();
var results = products.Where(filter).ToList();
```

This is verbose compared to writing `p => p.Price > 50`, but it lets you build predicates at runtime based on user input, configuration, or other dynamic values.

## Building a Dynamic Filter

Here's a practical example: a method that builds a filter predicate from a list of field-value pairs:

```csharp
public static Expression<Func<T, bool>> BuildFilter<T>(
    IEnumerable<(string Field, object Value)> filters)
{
    var parameter = Expression.Parameter(typeof(T), "x");
    Expression? combined = null;

    foreach (var (field, value) in filters)
    {
        var property = Expression.Property(parameter, field);
        var constant = Expression.Constant(
            Convert.ChangeType(value, property.Type));
        var equality = Expression.Equal(property, constant);

        combined = combined == null
            ? equality
            : Expression.AndAlso(combined, equality);
    }

    // If no filters, return x => true
    combined ??= Expression.Constant(true);

    return Expression.Lambda<Func<T, bool>>(combined, parameter);
}
```

Usage:

```csharp
var filters = new[]
{
    ("Status", (object)"Active"),
    ("Region", (object)"NZ")
};

Expression<Func<Customer, bool>> predicate = BuildFilter<Customer>(filters);

// Works with LINQ to Objects
var local = customers.Where(predicate.Compile()).ToList();

// Works with EF Core — translated to SQL
var dbResults = await context.Customers.Where(predicate).ToListAsync();
```

The same expression works both in-memory and in EF Core because EF Core's query provider walks the expression tree and generates SQL from it — it never compiles the delegate.

## Expression Visitors

`ExpressionVisitor` is the standard way to transform or analyse an entire expression tree. You subclass it and override the visit methods for the node types you care about:

```csharp
public class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParam;
    private readonly Expression _newValue;

    public ParameterReplacer(ParameterExpression oldParam, Expression newValue)
    {
        _oldParam = oldParam;
        _newValue = newValue;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        return node == _oldParam ? _newValue : base.VisitParameter(node);
    }
}
```

A common use case is combining two predicates that share the same parameter:

```csharp
public static Expression<Func<T, bool>> And<T>(
    this Expression<Func<T, bool>> first,
    Expression<Func<T, bool>> second)
{
    // Replace second's parameter with first's parameter
    var visitor = new ParameterReplacer(second.Parameters[0], first.Parameters[0]);
    var secondBody = visitor.Visit(second.Body);

    return Expression.Lambda<Func<T, bool>>(
        Expression.AndAlso(first.Body, secondBody),
        first.Parameters[0]);
}
```

Now you can combine EF Core-compatible predicates:

```csharp
Expression<Func<Product, bool>> inStock = p => p.Stock > 0;
Expression<Func<Product, bool>> affordable = p => p.Price < 200;

Expression<Func<Product, bool>> combined = inStock.And(affordable);

// Translates to a single SQL WHERE clause
var products = await context.Products.Where(combined).ToListAsync();
```

Without the parameter replacement, EF Core would throw because the two lambda parameters are different objects even though they represent the same thing.

## When Expression Trees Actually Matter

Expression trees are overkill for most day-to-day code. They're the right tool in a few specific situations:

**LINQ provider development** — Building a translator that converts expression trees to SQL, Elasticsearch queries, or some other query language. This is exactly what EF Core, LINQ to SQL, and similar libraries do.

**Dynamic filtering and sorting** — When users can pick fields and conditions at runtime and you need the query to run on the database (not in memory). `BuildFilter<T>` above is the pattern.

**Specification pattern with EF Core** — Reusable, composable query specs that don't lose the database translation:

```csharp
public class ActiveCustomerSpec
{
    public Expression<Func<Customer, bool>> Criteria =>
        c => c.IsActive && c.CreatedAt > DateTime.UtcNow.AddYears(-2);
}
```

**Avoiding stringly-typed member access** — Using `Expression<Func<T, TMember>>` to reference a property by name at compile time, instead of passing a string. This is how `INotifyPropertyChanged` helpers and some validation libraries work.

## What You Can't Do

Not everything compiles to an expression tree. Complex constructs like multi-statement lambda bodies, `await`, `out` parameters, and many method calls that have no LINQ provider mapping won't work. EF Core will throw at runtime (not compile time) if you use a method it can't translate:

```csharp
// This compiles but throws at runtime with EF Core
var results = await context.Orders
    .Where(o => IsHighValueOrder(o.Total))  // can't translate
    .ToListAsync();
```

The rule of thumb: if you're writing an expression to be consumed by a LINQ provider, use only the operations the provider documents as supported. If you need arbitrary logic, evaluate the complex parts in memory after the database query.

## Wrapping Up

Expression trees are one of the more unusual parts of C# — code that represents itself as data. You don't need them often, but when you do (dynamic database queries, reusable EF Core specifications, query providers), there's no substitute.

The `Expression<TDelegate>` type is the entry point. Lambdas get you trees for free. `ExpressionVisitor` lets you transform them. `Expression.Lambda().Compile()` turns them back into callable delegates. And the whole thing underpins every LINQ provider you've ever used without thinking about it.
