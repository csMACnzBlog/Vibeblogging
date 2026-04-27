---
title: Cancellation Tokens in C#
date: 2026-04-27
tags: dotnet, csharp, async, tutorial
image: cancellation-tokens-in-csharp.png
---

Async code doesn't run forever in a vacuum. Users cancel requests, clients disconnect, timeouts fire, and applications shut down. Without a way to signal that a running operation should stop, you end up with work continuing long after anyone cares about the result — burning CPU, holding database connections, and making your service slower for everyone else.

Cancellation tokens are how .NET handles this. They're a lightweight, cooperative mechanism for propagating cancellation signals through async call chains. This post covers everything you need to use them effectively.

## The Basics: CancellationTokenSource and CancellationToken

There are two types involved. `CancellationTokenSource` is the thing that triggers cancellation. `CancellationToken` is the thing you pass around and check for cancellation.

```csharp
var cts = new CancellationTokenSource();
CancellationToken token = cts.Token;

// Later, when you want to cancel:
cts.Cancel();
```

The source and token are separate by design. You keep the source to yourself and hand out the token — callers can observe and react to cancellation but can't trigger it themselves.

## Passing Tokens to Async Methods

The convention in .NET is that any async method that might take a while should accept a `CancellationToken` as its last parameter, with a default of `default` (or `CancellationToken.None`):

```csharp
public async Task<List<Order>> GetOrdersAsync(
    int customerId,
    CancellationToken cancellationToken = default)
{
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QueryAsync<Order>(
        "SELECT * FROM Orders WHERE CustomerId = @id",
        new { id = customerId },
        cancellationToken: cancellationToken);
}
```

The `default` keyword here is equivalent to `CancellationToken.None` — a token that is never cancelled. This keeps the method convenient to call without a token, while supporting cancellation when one is provided.

Pass the token through every async call you make. Don't stop at the first level:

```csharp
public async Task<CustomerSummary> GetCustomerSummaryAsync(
    int customerId,
    CancellationToken cancellationToken = default)
{
    var customer = await _customerRepo.GetByIdAsync(customerId, cancellationToken);
    var orders = await GetOrdersAsync(customerId, cancellationToken);
    var balance = await _billingService.GetBalanceAsync(customerId, cancellationToken);

    return new CustomerSummary(customer, orders, balance);
}
```

If the token is cancelled halfway through, the next awaited call that checks it will throw `OperationCanceledException` and the whole operation unwinds naturally.

## Checking for Cancellation

If you're doing CPU-bound work in a loop rather than awaiting async calls, you need to check the token yourself:

```csharp
public async Task ProcessItemsAsync(
    IEnumerable<Item> items,
    CancellationToken cancellationToken = default)
{
    foreach (var item in items)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await DoExpensiveWorkAsync(item, cancellationToken);
    }
}
```

`ThrowIfCancellationRequested()` throws `OperationCanceledException` if the token has been cancelled. Call it at the top of loops or between expensive steps.

If you need to branch based on cancellation rather than throw, check `IsCancellationRequested`:

```csharp
while (queue.TryDequeue(out var item))
{
    if (cancellationToken.IsCancellationRequested)
    {
        // Clean up and return rather than throwing
        await FlushPartialResultsAsync();
        return;
    }

    Process(item);
}
```

Use `ThrowIfCancellationRequested` by default. Only use `IsCancellationRequested` when you genuinely need a graceful cleanup path before stopping.

## Timeouts

`CancellationTokenSource` has built-in support for automatic cancellation after a delay:

```csharp
// Cancel after 30 seconds
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    var result = await FetchExternalDataAsync(cts.Token);
    return result;
}
catch (OperationCanceledException)
{
    throw new TimeoutException("External data fetch timed out after 30 seconds.");
}
```

Or you can set the timeout after creation:

```csharp
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(30));
```

This is the idiomatic way to implement timeouts in .NET — no `Task.WhenAny` juggling required.

## Linking Tokens

Sometimes you need an operation to be cancellable by two different sources: an explicit user cancellation *and* a timeout. `CancellationTokenSource.CreateLinkedTokenSource` creates a combined token that fires when either source cancels:

```csharp
public async Task<SearchResults> SearchAsync(
    string query,
    CancellationToken userCancellation = default)
{
    // Apply a per-search timeout, but also respect the caller's cancellation
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        userCancellation,
        timeoutCts.Token);

    return await _searchEngine.ExecuteAsync(query, linkedCts.Token);
}
```

The linked token cancels if the user cancels, if the 10-second timeout fires, or if either source is disposed. Disposing `linkedCts` doesn't cancel the original `userCancellation` — the link is one-way.

## Cancellation in ASP.NET Core

ASP.NET Core automatically cancels the request token when a client disconnects. You can access it through `HttpContext.RequestAborted` or, more conveniently, by declaring it as a parameter in a minimal API handler or controller action:

```csharp
app.MapGet("/api/products/search", async (
    [FromQuery] string q,
    ISearchService search,
    CancellationToken cancellationToken) =>
{
    var results = await search.SearchAsync(q, cancellationToken);
    return Results.Ok(results);
});
```

The framework binds `CancellationToken` parameters automatically to `HttpContext.RequestAborted`. If the browser navigates away or the connection drops, the token fires and your handler's awaited operations throw `OperationCanceledException` — freeing the thread and database connection immediately rather than completing work nobody will receive.

For controller actions it's the same:

```csharp
[HttpGet("search")]
public async Task<IActionResult> Search(
    [FromQuery] string q,
    CancellationToken cancellationToken)
{
    var results = await _searchService.SearchAsync(q, cancellationToken);
    return Ok(results);
}
```

## Handling OperationCanceledException

When a token fires, any async operation observing it throws `OperationCanceledException`. In most cases you don't handle this — you let it propagate up to wherever you want the cancellation to stop (usually a request handler or background service loop).

ASP.NET Core handles it gracefully: if the exception propagates out of a controller or minimal API handler, the framework returns a 499 or 500 response (depending on the host) and doesn't log it as an unhandled error.

Where you do need to handle it is in background service loops where you don't want a single cancelled operation to kill the whole service:

```csharp
public class DataSyncService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncDataAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown — exit the loop cleanly
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed, retrying after delay");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
```

Catching `OperationCanceledException` specifically (not `Exception`) is important here. When `stoppingToken` fires during `Task.Delay`, you want to exit cleanly. You don't want to swallow it inside the general `Exception` handler and restart the loop.

## Registering Callbacks

You can register a callback that fires synchronously when a token is cancelled:

```csharp
using var registration = cancellationToken.Register(() =>
{
    _logger.LogInformation("Operation cancelled, releasing resources");
    _semaphore.Release();
});
```

The callback runs on the thread that calls `Cancel()`, so keep it short. This is useful for integrating with APIs that don't natively support cancellation tokens — you can bridge cancellation to a separate signalling mechanism.

## Wrapping Up

Cancellation tokens are one of those things that seem optional until they're not. When you're running five database queries per request and a client disconnects halfway through, you want those queries to stop. When a background job is taking 90 seconds instead of 5, you want a timeout to fire. When your app is shutting down, you want in-flight work to complete or abort cleanly.

The mechanics are straightforward: create a `CancellationTokenSource`, pass `Token` through every async method, and let the framework or the token itself handle the rest. The more consistently you thread tokens through your async code from the start, the less work it is to add cancellation support later.

ASP.NET Core wires this up for free — you just need to declare the `CancellationToken` parameter and not ignore it.
