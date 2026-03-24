---
title: Producer-Consumer with Channels in C#
date: 2026-03-24
tags: csharp, dotnet, async, concurrency
image: producer-consumer-with-channels-in-csharp.png
---

If you've ever needed to pass data between tasks running at different speeds, you've probably reached for a `ConcurrentQueue\<T\>` and a bunch of manual signalling code. There's a cleaner way: `System.Threading.Channels`. It's built into .NET and it makes producer-consumer pipelines almost pleasant to write.

## What's a Channel?

A channel is a thread-safe data structure that lets one or more producers write values and one or more consumers read them. Think of it like a pipe: you write data into one end, and it comes out the other end as it's consumed.

.NET's `Channel\<T\>` gives you:

- An async-friendly API built on `IAsyncEnumerable\<T\>` and `ValueTask`
- Bounded or unbounded capacity
- Backpressure support out of the box

## Creating a Channel

You create channels via the `Channel` factory class:

```csharp
// Unbounded: accepts as many items as you write
var unbounded = Channel.CreateUnbounded<string>();

// Bounded: blocks or drops items when full
var bounded = Channel.CreateBounded<string>(capacity: 100);
```

The channel exposes two halves:

```csharp
ChannelWriter<string> writer = channel.Writer;
ChannelReader<string> reader = channel.Reader;
```

You typically hand the writer to your producer code and the reader to your consumer code. They never need to share state directly.

## A Simple Example

Here's the classic scenario: one task generating work items, another processing them.

```csharp
var channel = Channel.CreateUnbounded<int>();

var producer = Task.Run(async () =>
{
    for (int i = 0; i < 10; i++)
    {
        await channel.Writer.WriteAsync(i);
        Console.WriteLine($"Produced: {i}");
        await Task.Delay(100); // simulate work
    }

    channel.Writer.Complete(); // signal we're done
});

var consumer = Task.Run(async () =>
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        Console.WriteLine($"Consumed: {item}");
        await Task.Delay(200); // simulate slower processing
    }
});

await Task.WhenAll(producer, consumer);
```

A few things to notice. The producer calls `Complete()` when it's finished writing — that's the signal for the consumer to stop once it's drained the channel. The consumer uses `ReadAllAsync()`, which returns an `IAsyncEnumerable\<T\>` and waits efficiently for new items rather than spinning.

## Handling Backpressure

When you use a bounded channel and the consumer is slower than the producer, you need to decide what happens when the channel is full. There are two options: wait, or drop.

```csharp
var options = new BoundedChannelOptions(capacity: 10)
{
    FullMode = BoundedChannelFullMode.Wait   // default: await until space
    // or: DropOldest, DropNewest, DropWrite
};

var channel = Channel.CreateBounded<string>(options);
```

`Wait` is the safest option — it applies backpressure, slowing down the producer automatically. The drop modes are useful when you can afford to lose data, like in telemetry or UI refresh scenarios where only the latest value matters.

## Multiple Consumers

One of the best things about channels is how easy it is to scale consumption. You can spin up multiple consumers reading from the same channel and they'll divide the work automatically:

```csharp
var channel = Channel.CreateUnbounded<WorkItem>();

// Start 4 consumers in parallel
var consumers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
})).ToArray();

// Producer writes items
await foreach (var item in GetWorkItemsAsync())
{
    await channel.Writer.WriteAsync(item);
}

channel.Writer.Complete();
await Task.WhenAll(consumers);
```

Each `ReadAllAsync()` call competes for items — once a consumer takes an item, no other consumer sees it. You get natural load balancing with almost no extra code.

## Chaining Channels Into a Pipeline

Where channels really shine is when you chain them together. Each stage reads from one channel and writes to the next, forming a processing pipeline:

```csharp
static async Task RunPipelineAsync(IAsyncEnumerable<string> input)
{
    var parseChannel = Channel.CreateBounded<ParsedRecord>(50);
    var enrichChannel = Channel.CreateBounded<EnrichedRecord>(50);

    // Stage 1: parse raw strings
    var parseStage = Task.Run(async () =>
    {
        await foreach (var line in input)
        {
            var record = ParseLine(line);
            await parseChannel.Writer.WriteAsync(record);
        }
        parseChannel.Writer.Complete();
    });

    // Stage 2: enrich parsed records
    var enrichStage = Task.Run(async () =>
    {
        await foreach (var record in parseChannel.Reader.ReadAllAsync())
        {
            var enriched = await EnrichAsync(record);
            await enrichChannel.Writer.WriteAsync(enriched);
        }
        enrichChannel.Writer.Complete();
    });

    // Stage 3: save enriched records
    var saveStage = Task.Run(async () =>
    {
        await foreach (var enriched in enrichChannel.Reader.ReadAllAsync())
        {
            await SaveAsync(enriched);
        }
    });

    await Task.WhenAll(parseStage, enrichStage, saveStage);
}
```

Each stage runs concurrently and the channels between them buffer data and regulate flow. If saving is slow, `enrichChannel` fills up and enriching slows down, which in turn slows down parsing. The pipeline self-regulates.

## Error Handling

What happens when a stage fails? You should propagate the error through the channel so downstream stages shut down cleanly rather than waiting forever:

```csharp
var channel = Channel.CreateUnbounded<string>();

var producer = Task.Run(async () =>
{
    try
    {
        await foreach (var item in GetItemsAsync())
        {
            await channel.Writer.WriteAsync(item);
        }
        channel.Writer.Complete();
    }
    catch (Exception ex)
    {
        channel.Writer.Complete(ex); // pass the exception downstream
    }
});

var consumer = Task.Run(async () =>
{
    try
    {
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            await ProcessAsync(item);
        }
    }
    catch (ChannelClosedException ex) when (ex.InnerException != null)
    {
        Console.Error.WriteLine($"Pipeline failed: {ex.InnerException.Message}");
    }
});
```

`Writer.Complete(exception)` closes the channel with a fault. The next `ReadAllAsync()` iteration will throw a `ChannelClosedException` wrapping the original exception.

## When to Use Channels

Channels are great when:

- You're decoupling producers and consumers that run at different speeds
- You need a pipeline of processing stages
- You want built-in backpressure without rolling your own semaphores
- You're working with `async`/`await` throughout

They're less ideal if you need pub/sub (multiple independent consumers each seeing all items — consider `System.Reactive` or `IObservable\<T\>` for that) or if your use case is purely synchronous (a plain `ConcurrentQueue\<T\>` might be simpler).

Channels sit in a sweet spot: more structured than raw `ConcurrentQueue\<T\>`, more lightweight than a full message broker, and async-native throughout. Once you spot the producer-consumer pattern in your code, you'll reach for them instinctively.
