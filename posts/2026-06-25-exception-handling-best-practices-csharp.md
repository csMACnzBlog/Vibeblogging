---
title: Exception Handling Best Practices in C#
date: 2026-06-25
tags: csharp, dotnet, error-handling, best-practices
image: exception-handling-best-practices-csharp.png
---

Exceptions are for *exceptional* paths, but the way you handle them shapes your diagnostics, API behavior, and on-call stress levels. If exceptions are noisy, vague, or swallowed, you're flying blind when things go wrong.

Let's walk through a few practical patterns that keep C# exception handling clean and predictable.

## Throw the most specific exception you can

`throw new Exception("bad input")` doesn't help callers much. Use a specific built-in type (or a custom domain exception) so intent is obvious.

```csharp
public static decimal CalculateDiscount(decimal price, decimal percentage)
{
    if (price < 0)
        throw new ArgumentOutOfRangeException(nameof(price), "Price must be non-negative.");

    if (percentage is < 0 or > 1)
        throw new ArgumentOutOfRangeException(nameof(percentage), "Percentage must be between 0 and 1.");

    return price * percentage;
}
```

Now callers can catch `ArgumentOutOfRangeException` when they need to handle bad input differently from system failures.

## Preserve stack traces when rethrowing

If you're inside a catch block and need upstream code to handle the same exception, rethrow with `throw;`.

```csharp
try
{
    await _paymentGateway.ChargeAsync(request, cancellationToken);
}
catch (Exception)
{
    _logger.LogWarning("Payment charge failed; retry policy will handle it.");
    throw; // Keeps original stack trace
}
```

Avoid `throw ex;` here — that resets the stack trace and makes debugging harder.

## Wrap low-level exceptions at boundaries

Inside infrastructure code, provider exceptions are fine. At service boundaries, convert them to domain-focused exceptions with context.

```csharp
public sealed class OrderPersistenceException : Exception
{
    public OrderPersistenceException(string message, Exception innerException)
        : base(message, innerException) { }
}

public async Task SaveAsync(Order order, CancellationToken cancellationToken)
{
    try
    {
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException ex)
    {
        throw new OrderPersistenceException(
            $"Failed to persist order '{order.Id}'.",
            ex);
    }
}
```

The caller now gets domain language *and* the original exception via `InnerException`.

## Use exception filters for targeted handling

Filters keep catch logic focused and avoid giant catch blocks.

```csharp
try
{
    await _inventoryClient.ReserveStockAsync(sku, quantity, cancellationToken);
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
{
    return ReservationResult.OutOfStock(sku);
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    return ReservationResult.UnknownSku(sku);
}
```

This is often cleaner than catching once and switching inside the catch body.

## Log once, close to ownership

A common anti-pattern is logging the same exception in every layer. That creates duplicate noise and hides the real signal.

A useful rule of thumb:

- **Inner layers**: add context by wrapping exceptions when needed
- **Outer boundary** (API, worker loop, command handler): log the final failure once

That keeps logs readable and still preserves full error history.

## Final thought

Solid exception handling isn't about adding more `try/catch`. It's about making failure modes explicit: specific exception types, preserved stack traces, meaningful context, and clear ownership of logging.

If you do those four things consistently, production incidents get a lot less mysterious.
