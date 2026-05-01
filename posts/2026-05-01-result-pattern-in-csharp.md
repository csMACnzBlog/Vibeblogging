---
title: The Result Pattern in C#
date: 2026-05-01
tags: dotnet, csharp, architecture, tutorial
image: result-pattern-in-csharp.png
---

Throwing exceptions for expected failure cases has always felt a bit off. An exception should be exceptional — something you genuinely didn't anticipate. But in most applications, "user not found" or "invalid input" isn't exceptional at all. It's a normal outcome that callers need to handle.

The Result pattern gives you a way to make failures explicit in your method signatures. Instead of `User GetUser(int id)` (which might throw), you return something like `Result<User>` that forces the caller to consider both paths. The failure case is right there in the type — impossible to ignore.

## A Simple Result Type

The core idea is a type that represents either success or failure:

```csharp
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private Result(string error)
    {
        IsSuccess = false;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(error);
}
```

The private constructors enforce the invariant: you're either a success with a value, or a failure with an error. You can't accidentally construct an invalid state.

Usage is straightforward:

```csharp
public Result<User> GetUser(int id)
{
    var user = _db.Users.Find(id);
    if (user is null)
        return Result<User>.Fail($"User {id} not found");

    return Result<User>.Ok(user);
}
```

The caller must actively check `IsSuccess` before accessing `Value`. The failure case can't be silently swallowed the way a missing null check can be.

## Making It Nicer with Records

The class version above works but has some boilerplate. C# records and generics let you tighten it up considerably:

```csharp
public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;
    public sealed record Fail(string Error) : Result<T>;

    public static Result<T> Success(T value) => new Ok(value);
    public static Result<T> Failure(string error) => new Fail(error);
}
```

Now the two cases are distinct types, which pairs beautifully with pattern matching:

```csharp
var result = GetUser(42);

var message = result switch
{
    Result<User>.Ok ok => $"Found: {ok.Value.Name}",
    Result<User>.Fail fail => $"Error: {fail.Error}",
    _ => "Unknown"
};
```

The compiler can exhaust all cases for you, and you don't need to remember to check `IsSuccess` first. If you forget to handle the `Fail` case, you get a compiler warning.

## Typed Errors

String error messages are fine to get started, but they're hard to react to programmatically. If you want callers to be able to distinguish a "not found" from an "unauthorised" from a "validation failed", you need a richer error type:

```csharp
public abstract record Error
{
    public sealed record NotFound(string Message) : Error;
    public sealed record Unauthorised(string Message) : Error;
    public sealed record Validation(string Field, string Message) : Error;
}
```

Update `Result<T>` to carry an `Error` instead of a string:

```csharp
public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;
    public sealed record Fail(Error Error) : Result<T>;

    public static Result<T> Success(T value) => new Ok(value);
    public static Result<T> Failure(Error error) => new Fail(error);
}
```

Now the calling code can pattern match on the error type too:

```csharp
var result = await GetUserAsync(userId);

return result switch
{
    Result<User>.Ok ok => Results.Ok(ok.Value),
    Result<User>.Fail { Error: Error.NotFound msg } => Results.NotFound(msg.Message),
    Result<User>.Fail { Error: Error.Unauthorised msg } => Results.Unauthorized(),
    Result<User>.Fail { Error: Error.Validation v } =>
        Results.BadRequest(new { Field = v.Field, v.Message }),
    _ => Results.Problem("An unexpected error occurred")
};
```

This is the kind of exhaustive, compiler-checked error handling that exceptions can't give you.

## Composing Results

One friction point with the Result pattern is chaining operations. If every method returns a `Result<T>`, you end up with nested checks:

```csharp
var userResult = GetUser(id);
if (userResult is Result<User>.Fail fail)
    return Result<Order>.Failure(fail.Error);

var user = ((Result<User>.Ok)userResult).Value;
var orderResult = GetLatestOrder(user);
// ... and so on
```

A `Map` method helps — it applies a function to the success value and propagates failures unchanged:

```csharp
public abstract record Result<T>
{
    // ... previous members ...

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) => this switch
    {
        Ok ok => Result<TOut>.Success(mapper(ok.Value)),
        Fail fail => Result<TOut>.Failure(fail.Error),
        _ => throw new InvalidOperationException()
    };

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder) => this switch
    {
        Ok ok => binder(ok.Value),
        Fail fail => Result<TOut>.Failure(fail.Error),
        _ => throw new InvalidOperationException()
    };
}
```

`Map` transforms the value. `Bind` chains an operation that itself returns a Result (avoiding `Result<Result<T>>`):

```csharp
var result = GetUser(id)
    .Bind(user => GetLatestOrder(user))
    .Map(order => new OrderSummary(order.Id, order.Total));
```

Each step short-circuits on failure. If `GetUser` fails, neither `GetLatestOrder` nor the final `Map` runs. The error propagates through unchanged.

## Integrating with Minimal APIs

Results work especially well in Minimal APIs. You can map from your domain `Result<T>` to `IResult` in one place:

```csharp
public static IResult ToHttpResult<T>(this Result<T> result) => result switch
{
    Result<T>.Ok ok => Results.Ok(ok.Value),
    Result<T>.Fail { Error: Error.NotFound msg } => Results.NotFound(new { msg.Message }),
    Result<T>.Fail { Error: Error.Unauthorised } => Results.Unauthorized(),
    Result<T>.Fail { Error: Error.Validation v } =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [v.Field] = [v.Message] }),
    _ => Results.Problem("An unexpected error occurred")
};
```

Your endpoint handlers stay focused on the business logic:

```csharp
app.MapGet("/users/{id}", async (int id, UserService users) =>
{
    var result = await users.GetUserAsync(id);
    return result.ToHttpResult();
});

app.MapPost("/orders", async (CreateOrderRequest req, OrderService orders) =>
{
    var result = await orders.CreateOrderAsync(req);
    return result.ToHttpResult();
});
```

The HTTP status code logic lives in one place. Every endpoint that uses a `Result<T>` gets consistent error responses automatically.

## Should You Use a Library?

Rolling your own `Result<T>` is easy enough, but a few libraries are worth knowing about.

**ErrorOr** (`ErrorOr` on NuGet) provides a source-generated `ErrorOr<T>` type with a similar API. It handles async chains with `ThenAsync`, has built-in error types, and integrates with `IActionResult`. If you're starting a new project and don't want to maintain your own implementation, it's a solid choice.

**FluentResults** is older and heavier but more flexible — it supports multiple errors per result and has an extension model for custom metadata. Useful if you need to accumulate a list of validation errors rather than fail-fast on the first one.

For most projects, the hand-rolled version in this post is genuinely enough. The important thing is consistency — pick one approach and use it everywhere rather than mixing exceptions and Results.

## When to Use It (and When Not To)

The Result pattern is a good fit when:

- Failure is a normal, expected part of the domain (not found, validation failures, business rule violations)
- You want callers to be forced to handle both paths
- You're building APIs where you need fine-grained control over HTTP responses
- You want to compose multiple fallible operations without nested null checks

It's not a great fit when:

- You're dealing with genuinely unexpected failures (database connections dropping, out-of-memory conditions) — those are still exceptions
- You're writing library code that throws to let callers decide how to handle errors
- The overhead of the pattern adds noise without adding clarity in simple scripts or utilities

## Wrapping Up

The Result pattern is one of those ideas that seems overly ceremonious until the first time you trace a bug caused by an unhandled exception that should have been a predictable failure. Making the failure case a first-class part of your method signatures is a small change with a noticeable effect on code clarity.

Start simple — even just a `Result<T>` with a string error is an improvement over unchecked nulls and surprise exceptions. Add typed errors when you need callers to react differently to different failures. Add `Map` and `Bind` when composition becomes unwieldy. The pattern scales to whatever complexity you actually need.
