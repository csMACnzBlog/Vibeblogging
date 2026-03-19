---
title: Pattern Matching in C#
date: 2026-03-19
tags: dotnet, csharp, patterns, language-features
image: pattern-matching-in-csharp.png
---

If you've ever written a long chain of `if`/`else if` blocks just to check what type something is or inspect a few properties, pattern matching is for you. C# has been adding pattern matching features since C# 7, and by C# 11 they're genuinely powerful. Let's walk through each one with examples that build on each other.

## Switch Expressions

The classic switch statement works fine, but switch expressions are more concise and force you to handle every case. Here's the difference:

```csharp
// Old switch statement
string GetLabel(int priority)
{
    switch (priority)
    {
        case 1: return "Critical";
        case 2: return "High";
        case 3: return "Medium";
        default: return "Low";
    }
}

// Switch expression — much cleaner
string GetLabel(int priority) => priority switch
{
    1 => "Critical",
    2 => "High",
    3 => "Medium",
    _ => "Low"
};
```

The `_` at the end is the discard pattern — it matches anything. If you leave it out and no arm matches at runtime, you get an `InvalidOperationException`. The compiler will warn you if it can tell your switch isn't exhaustive.

## Type Patterns

Type patterns let you match on what something *is*, not just what it equals. The `is` expression has supported this since C# 7:

```csharp
object shape = GetShape();

if (shape is Circle c)
    Console.WriteLine($"Circle with radius {c.Radius}");
else if (shape is Rectangle r)
    Console.WriteLine($"Rectangle {r.Width}x{r.Height}");
```

In a switch expression, it reads even better:

```csharp
double GetArea(object shape) => shape switch
{
    Circle c    => Math.PI * c.Radius * c.Radius,
    Rectangle r => r.Width * r.Height,
    Triangle t  => 0.5 * t.Base * t.Height,
    null        => throw new ArgumentNullException(nameof(shape)),
    _           => throw new ArgumentException("Unknown shape")
};
```

The variable after the type name (`c`, `r`, `t`) is scoped to that arm — you can use it immediately in the expression.

## Property Patterns

Property patterns let you match on an object's properties without first extracting them into variables. You use `{ Property: value }` syntax:

```csharp
string ClassifyOrder(Order order) => order switch
{
    { Status: OrderStatus.Cancelled }              => "Cancelled",
    { Status: OrderStatus.Shipped, IsExpress: true } => "Express in transit",
    { Status: OrderStatus.Shipped }                => "Standard in transit",
    { Total: > 1000 }                              => "High-value pending",
    _                                              => "Standard pending"
};
```

You can nest property patterns too. If `order` has a `Customer` property with an `IsPremium` flag:

```csharp
{ Status: OrderStatus.Pending, Customer: { IsPremium: true } } => "Priority queue"
```

This is much cleaner than the equivalent `order.Status == OrderStatus.Pending && order.Customer?.IsPremium == true`.

## Positional Patterns

If a type has a `Deconstruct` method (or is a record, which gets one automatically), you can use positional patterns to match on its components:

```csharp
record Point(int X, int Y);

string Describe(Point p) => p switch
{
    (0, 0)  => "Origin",
    (0, _)  => "On Y axis",
    (_, 0)  => "On X axis",
    (> 0, > 0) => "Quadrant I",
    (< 0, > 0) => "Quadrant II",
    (< 0, < 0) => "Quadrant III",
    _          => "Quadrant IV"
};
```

Tuples work the same way without needing a dedicated type:

```csharp
string Classify(bool isAdmin, bool isActive) => (isAdmin, isActive) switch
{
    (true, true)  => "Active admin",
    (true, false) => "Inactive admin",
    (false, true) => "Active user",
    (false, false) => "Inactive user"
};
```

## List Patterns

C# 11 added list patterns, which match on the shape of a sequence. You can check length, specific elements, and use `..` to represent any number of elements in the middle:

```csharp
string DescribeList(int[] numbers) => numbers switch
{
    []           => "Empty",
    [var x]      => $"Single element: {x}",
    [var x, var y] => $"Two elements: {x} and {y}",
    [1, 2, ..]   => "Starts with 1, 2",
    [.., 99]     => "Ends with 99",
    [_, _, ..]   => "Three or more elements"
};
```

The `..` slice pattern is especially handy when you care about the head or tail of a sequence but not the middle.

## Combining Patterns

You can combine patterns with `and`, `or`, and `not`:

```csharp
bool IsWeekday(DayOfWeek day) => day is
    not (DayOfWeek.Saturday or DayOfWeek.Sunday);

string ClassifyTemperature(double temp) => temp switch
{
    < 0           => "Freezing",
    >= 0 and < 15 => "Cold",
    >= 15 and < 25 => "Comfortable",
    >= 25 and < 35 => "Warm",
    _             => "Hot"
};
```

The `and`/`or`/`not` keywords were added in C# 9. They make range checks readable without introducing temporary variables.

## Real-World Example: Handling HTTP Results

Let me tie this together with something you'd actually write. Imagine parsing an HTTP response into a domain result:

```csharp
record HttpResult(int StatusCode, string? Body, string? ErrorMessage);

sealed record ApiResponse<T>;
sealed record Success<T>(T Value) : ApiResponse<T>;
sealed record NotFound<T>() : ApiResponse<T>;
sealed record ValidationError<T>(string Message) : ApiResponse<T>;
sealed record ServerError<T>(string Detail) : ApiResponse<T>;

ApiResponse<User> ParseUserResponse(HttpResult result) => result switch
{
    { StatusCode: 200, Body: { } body }
        => new Success<User>(JsonSerializer.Deserialize<User>(body)!),

    { StatusCode: 404 }
        => new NotFound<User>(),

    { StatusCode: >= 400 and < 500, ErrorMessage: { } msg }
        => new ValidationError<User>(msg),

    { StatusCode: >= 500 }
        => new ServerError<User>(result.ErrorMessage ?? "Unknown server error"),

    _   => new ServerError<User>($"Unexpected status: {result.StatusCode}")
};
```

And then consuming it:

```csharp
void HandleResponse(ApiResponse<User> response)
{
    var message = response switch
    {
        Success<User> { Value: var user }            => $"Hello, {user.Name}!",
        NotFound<User>                               => "User not found.",
        ValidationError<User> { Message: var msg }   => $"Bad request: {msg}",
        ServerError<User> { Detail: var detail }     => $"Server error: {detail}"
    };

    Console.WriteLine(message);
}
```

This is a discriminated union pattern — every possible outcome is a distinct type, and pattern matching ensures you handle all of them. The compiler will warn you if you add a new subtype and forget to update the switch.

## When to Reach for Pattern Matching

Pattern matching shines when:

- You're branching on **type** (replacing `is`/cast chains)
- You're branching on **multiple properties** at once
- You want the compiler to tell you when you've missed a case
- You're building a pipeline that transforms data through multiple shapes

Traditional `if`/`else` is still fine for simple boolean checks or when you need early returns with side effects. But anywhere you find yourself writing `if (x is Foo foo && foo.Bar == something)`, a switch expression will be cleaner.

C# keeps adding to the pattern matching story with each release — relational patterns, list patterns, extended property patterns. It's worth keeping up with, because each feature tends to collapse a real category of boilerplate.
