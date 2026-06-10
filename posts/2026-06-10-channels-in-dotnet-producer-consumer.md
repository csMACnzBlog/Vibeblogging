---
title: "Channels in .NET: Async Producer-Consumer"
date: 2026-06-10
tags: csharp, dotnet, async, threading, performance
image: channels-in-dotnet-producer-consumer.png
---

Most of us have reached for `ConcurrentQueue<T>` or `BlockingCollection<T>` when we need to pass work between threads. They work, but they're built for synchronous consumption. If you want to await the next item — without burning a thread — you need `System.Threading.Channels`.

Channels landed in .NET Core 3.0 and they're the cleanest way to build async producer-consumer pipelines today.

## What is a Channel?

A `Channel<T>` is a concurrent queue with first-class `async`/`await` support. It has two ends:

- `ChannelWriter<T>` — the producer writes items in
- `ChannelReader<T>` — the consumer reads items out

The consumer can `await` the next item instead of spinning or blocking. The producer can `await` space to become available in bounded channels. No locks, no busy-waiting.

## Creating a channel

There are two flavours: unbounded and bounded.

```csharp
using System.Threading.Channels;

// Unbounded: accepts items without limit
var unbounded = Channel.CreateUnbounded<string>();

// Bounded: caps the queue at N items (applies backpressure to writers)
var bounded = Channel.CreateBounded<string>(capacity: 100);
```

For most real workloads, bounded channels are safer. Without a cap, a slow consumer and a fast producer can grow your in-memory queue indefinitely.

## Writing from a producer

```csharp
using System.Threading.Channels;

var channel = Channel.CreateBounded<WorkItem>(capacity: 50);

async Task ProduceAsync(ChannelWriter<WorkItem> writer, CancellationToken ct)
{
    try
    {
        for (int i = 0; i < 1000; i++)
        {
            var item = new WorkItem(Id: i, Payload: $"task-{i}");

            // Awaits if the channel is full (backpressure)
            await writer.WriteAsync(item, ct);
        }
    }
    finally
    {
        // Signal to consumers that no more items are coming
        writer.Complete();
    }
}
```

Calling `Complete()` is important. It lets consumers know the stream is finished so they don't wait forever for an item that'll never arrive.

## Reading from a consumer

```csharp
using System.Threading.Channels;

async Task ConsumeAsync(ChannelReader<WorkItem> reader, CancellationToken ct)
{
    // ReadAllAsync returns an IAsyncEnumerable<T>
    // It completes when the writer calls Complete()
    await foreach (var item in reader.ReadAllAsync(ct))
    {
        await ProcessAsync(item, ct);
    }
}
```

`ReadAllAsync` returns an `IAsyncEnumerable<T>`. It suspends without blocking when the channel is empty, wakes when new items arrive, and exits cleanly when the writer has completed.

## Putting it together: a background processing queue

Here's a practical pattern — a hosted service that processes items written to a channel by other parts of your app:

```csharp
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public sealed record WorkItem(int Id, string Payload);

public sealed class WorkQueue
{
    private readonly Channel<WorkItem> _channel =
        Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity: 200)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ChannelWriter<WorkItem> Writer => _channel.Writer;
    public ChannelReader<WorkItem> Reader => _channel.Reader;
}

public sealed class WorkQueueProcessor : BackgroundService
{
    private readonly WorkQueue _queue;

    public WorkQueueProcessor(WorkQueue queue)
    {
        _queue = queue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessItemAsync(item, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log and continue — don't let one bad item kill the loop
                Console.Error.WriteLine($"Failed to process item {item.Id}: {ex.Message}");
            }
        }
    }

    private static async Task ProcessItemAsync(WorkItem item, CancellationToken ct)
    {
        // Simulate some async work
        await Task.Delay(10, ct);
        Console.WriteLine($"Processed: {item.Payload}");
    }
}
```

And register everything in your DI container:

```csharp
builder.Services.AddSingleton<WorkQueue>();
builder.Services.AddHostedService<WorkQueueProcessor>();
```

Now any service in your app can inject `WorkQueue` and write to it:

```csharp
public class OrderController(WorkQueue queue) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SubmitOrder(OrderRequest request, CancellationToken ct)
    {
        var item = new WorkItem(Id: request.OrderId, Payload: request.Description);
        await queue.Writer.WriteAsync(item, ct);
        return Accepted();
    }
}
```

The controller doesn't know or care about the processing. It writes and moves on.

## Handling multiple consumers

If a single consumer is a bottleneck, you can spin up several. Set `SingleReader = false` in your options, then launch parallel consumer tasks:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var consumers = Enumerable.Range(0, Environment.ProcessorCount)
        .Select(_ => ConsumeAsync(stoppingToken));

    await Task.WhenAll(consumers);
}

private async Task ConsumeAsync(CancellationToken ct)
{
    await foreach (var item in _queue.Reader.ReadAllAsync(ct))
    {
        await ProcessItemAsync(item, ct);
    }
}
```

Each consumer pulls from the same channel. The channel handles the coordination.

## `BoundedChannelFullMode` options

When the channel is full, you get to choose what happens:

| Mode | Behaviour |
|---|---|
| `Wait` | Writer awaits until space is available |
| `DropOldest` | Discards the oldest item to make room |
| `DropNewest` | Discards the item being written |
| `DropWrite` | Silently drops the write and returns |

`Wait` is the right default when you can't afford to lose items. The others suit telemetry pipelines and similar scenarios where dropping is acceptable.

## When channels fit and when they don't

Channels are a great fit when:

- You need async consumption without blocking a thread
- There's a natural separation between producers and consumers
- You want built-in backpressure with bounded capacity

They're less useful when:

- You need a durable queue that survives process restarts (use a message broker like RabbitMQ or Azure Service Bus instead)
- You want fan-out to multiple independent subscribers (look at `IObservable<T>` or Dataflow for that)
- Work items need retry logic and scheduling (consider a library like Hangfire)

## Final thought

`System.Threading.Channels` gives you a clean async handoff between producers and consumers with minimal ceremony. It handles backpressure, cancellation, and clean shutdown — the parts that usually require careful thought when you roll your own queue.

If you've been using `ConcurrentQueue<T>` with a `Task.Delay` polling loop, this is worth the fifteen minutes to switch over.
