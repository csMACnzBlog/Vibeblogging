---
title: IAsyncEnumerable in C#
date: 2026-03-23
tags: csharp, dotnet, async, language-features
image: iasyncenumerable-in-csharp.png
---

If you've used `async`/`await` in C#, you know how to await a single value. But what about a *stream* of values that arrive asynchronously over time? That's where `IAsyncEnumerable\<T\>` comes in, and once you get it, you'll wonder how you lived without it.

## The Problem: Async Data Streams

Imagine you're reading records from a database, processing messages from a queue, or tailing a log file. The data arrives a piece at a time, and each piece might take a moment to arrive. The classic approach is to load everything into a `List\<T\>` first:

```csharp
public async Task<List<Order>> GetPendingOrdersAsync()
{
    var orders = new List<Order>();
    var reader = await _db.QueryAsync("SELECT * FROM orders WHERE status = 'pending'");
    while (await reader.ReadAsync())
    {
        orders.Add(MapOrder(reader));
    }
    return orders; // caller waits for everything before seeing anything
}
```

This works, but it has a latency problem. The caller can't start processing until *all* records are loaded. If there are 50,000 orders, you're burning memory and making the caller wait.

## Enter IAsyncEnumerable

`IAsyncEnumerable\<T\>` is the async counterpart to `IEnumerable\<T\>`. It lets a method produce items one at a time, asynchronously. You use `yield return` just like with regular iterators, but the method is `async`:

```csharp
public async IAsyncEnumerable<Order> GetPendingOrdersAsync()
{
    var reader = await _db.QueryAsync("SELECT * FROM orders WHERE status = 'pending'");
    while (await reader.ReadAsync())
    {
        yield return MapOrder(reader);
    }
}
```

The caller iterates with `await foreach`:

```csharp
await foreach (var order in GetPendingOrdersAsync())
{
    await ProcessOrderAsync(order);
}
```

Now each order is processed as soon as it arrives, rather than waiting for the full result set. For 50,000 records, that's a meaningful improvement.

## A Realistic Example: Paginated API Calls

Let's say you're fetching all customers from a REST API that returns pages of 100 results. You want to process each customer as it arrives without accumulating the full list:

```csharp
public async IAsyncEnumerable<Customer> GetAllCustomersAsync(
    HttpClient client,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    int page = 1;
    bool hasMore = true;

    while (hasMore)
    {
        var response = await client.GetFromJsonAsync<PagedResult<Customer>>(
            $"/api/customers?page={page}&size=100",
            cancellationToken);

        foreach (var customer in response!.Items)
        {
            yield return customer;
        }

        hasMore = response.HasNextPage;
        page++;
    }
}
```

Notice the `[EnumeratorCancellation]` attribute on the `CancellationToken` parameter. This wires cancellation into the `await foreach` loop automatically:

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

await foreach (var customer in GetAllCustomersAsync(client, cts.Token))
{
    await SendWelcomeEmailAsync(customer);
}
```

If the token is cancelled (or the timeout fires), the loop stops cleanly.

## Filtering and Transforming Async Streams

LINQ doesn't work directly with `IAsyncEnumerable\<T\>` in the standard library, but the `System.Linq.Async` NuGet package adds full LINQ support:

```csharp
// Install: dotnet add package System.Linq.Async

var highValueOrders = GetPendingOrdersAsync()
    .Where(o => o.Total > 1000m)
    .OrderByDescending(o => o.Total)
    .Take(10);

await foreach (var order in highValueOrders)
{
    Console.WriteLine($"{order.Id}: {order.Total:C}");
}
```

Without the package, you can write simple extensions yourself:

```csharp
public static async IAsyncEnumerable<TResult> SelectAsync<T, TResult>(
    this IAsyncEnumerable<T> source,
    Func<T, TResult> selector)
{
    await foreach (var item in source)
    {
        yield return selector(item);
    }
}
```

## Handling Errors and Cleanup

Because `IAsyncEnumerable\<T\>` methods are async iterators, you can use `try`/`finally` for cleanup:

```csharp
public async IAsyncEnumerable<LogEntry> TailLogFileAsync(string path)
{
    using var reader = new StreamReader(path);
    
    try
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is not null)
            {
                yield return ParseLogEntry(line);
            }
            else
            {
                await Task.Delay(100); // wait a bit before polling again
            }
        }
    }
    finally
    {
        // reader is disposed here even if the caller stops iterating early
    }
}
```

The `finally` block runs when the iterator is disposed — whether that's because you iterated to the end or broke out early.

## Producing and Consuming Concurrently

One pattern you'll run into is wanting to produce items on a background task while consuming them on the calling thread. `System.Threading.Channels` pairs beautifully with `IAsyncEnumerable\<T\>` here:

```csharp
public async IAsyncEnumerable<WorkItem> ProduceWorkItemsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var channel = Channel.CreateUnbounded<WorkItem>();

    // Start producer in background
    _ = Task.Run(async () =>
    {
        try
        {
            await foreach (var item in _queue.ReceiveAllAsync(ct))
            {
                await channel.Writer.WriteAsync(item, ct);
            }
        }
        finally
        {
            channel.Writer.Complete();
        }
    }, ct);

    // Expose as async stream
    await foreach (var item in channel.Reader.ReadAllAsync(ct))
    {
        yield return item;
    }
}
```

`channel.Reader.ReadAllAsync()` already returns an `IAsyncEnumerable\<T\>`, so this is just forwarding through, but the pattern scales well when you need to add buffering, batching, or transformation in between.

## When to Use IAsyncEnumerable

It's the right tool when:

- You're reading from a database, file, or network and want to process results as they arrive
- You're calling a paginated API and want to abstract away the pagination
- You're building a pipeline where data flows through multiple async stages
- You want to expose a stream of events or notifications

It's *not* necessary when you have a small, bounded result set that's cheap to load all at once — in that case, a `List\<T\>` is simpler.

## A Quick Gotcha: No Parallel Enumeration

One thing to watch out for: `IAsyncEnumerable\<T\>` is sequential by design. You can't iterate the same stream from two places at once. If you need parallel processing of stream items, pull them into a `Channel\<T\>` and use multiple consumers, or use `Parallel.ForEachAsync`:

```csharp
await Parallel.ForEachAsync(
    GetPendingOrdersAsync(),
    new ParallelOptions { MaxDegreeOfParallelism = 4 },
    async (order, ct) =>
    {
        await ProcessOrderAsync(order);
    });
```

`Parallel.ForEachAsync` accepts `IAsyncEnumerable\<T\>` directly — it's one of those small .NET 6 additions that quietly made a big difference.

`IAsyncEnumerable\<T\>` fills the gap between "I have a result" (`Task\<T\>`) and "I have many results eventually" (async streams). Once you start applying it to database queries, API clients, and message consumers, synchronous bulk loading starts to feel like a step backward.
