---
title: Outbox Pattern for Reliable Messaging in .NET
date: 2026-05-11
tags: dotnet, aspnetcore, architecture, messaging
image: outbox-pattern-reliable-messaging-dotnet.png
---

If you've ever saved data to your database and then tried to publish an event right after, you've probably wondered: what happens if the app crashes between those two steps? That's the classic dual-write problem, and it's exactly what the Outbox Pattern solves.

The idea is simple: write your domain change and the message to an outbox table in the same transaction. Then a background worker reliably publishes pending outbox messages.

## The Problem with Direct Publish

Here's the risky version most of us write first:

```csharp
public async Task PlaceOrderAsync(PlaceOrderCommand command, CancellationToken ct)
{
    var order = new Order(command.CustomerId, command.Total);

    _dbContext.Orders.Add(order);
    await _dbContext.SaveChangesAsync(ct);

    // If this fails, the order is saved but no event is published.
    await _publisher.PublishAsync(new OrderPlaced(order.Id), ct);
}
```

If `PublishAsync` fails, your data and your integration events drift apart.

## Step 1: Add an Outbox Message Model

Start with a table-backed model for pending messages:

```csharp
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
}
```

This is intentionally boring. Boring is good here.

## Step 2: Save Domain Data + Outbox in One Transaction

Now write both records together:

```csharp
public async Task PlaceOrderAsync(PlaceOrderCommand command, CancellationToken ct)
{
    var order = new Order(command.CustomerId, command.Total);
    var integrationEvent = new OrderPlaced(order.Id, order.CustomerId, order.Total);

    var outbox = new OutboxMessage
    {
        Type = nameof(OrderPlaced),
        Payload = JsonSerializer.Serialize(integrationEvent)
    };

    await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

    _dbContext.Orders.Add(order);
    _dbContext.Set<OutboxMessage>().Add(outbox);

    await _dbContext.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
}
```

Once this transaction commits, you know both the business state and the message record exist.

## Step 3: Publish from a Background Worker

A hosted service can poll and publish unprocessed messages:

```csharp
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventPublisher _publisher;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, IEventPublisher publisher)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var messages = await dbContext.Set<OutboxMessage>()
                .Where(x => x.ProcessedOnUtc == null)
                .OrderBy(x => x.OccurredOnUtc)
                .Take(20)
                .ToListAsync(stoppingToken);

            foreach (var message in messages)
            {
                try
                {
                    if (message.Type == nameof(OrderPlaced))
                    {
                        var @event = JsonSerializer.Deserialize<OrderPlaced>(message.Payload);
                        if (@event is not null)
                        {
                            await _publisher.PublishAsync(@event, stoppingToken);
                        }
                    }

                    message.ProcessedOnUtc = DateTime.UtcNow;
                    message.Error = null;
                }
                catch (Exception ex)
                {
                    message.Error = ex.Message;
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
```

This gives you retries on the next loop automatically for failed rows.

## Production Details Worth Adding

The core pattern is tiny, but production systems usually add a few extras:

- **Idempotency** in consumers (assume duplicates can happen)
- **Retry backoff** so failing messages don't hammer dependencies
- **Locking strategy** when multiple app instances process the outbox
- **Cleanup job** for old processed rows
- **Structured logging/metrics** on publish success and failure rates

## Wrapping Up

The Outbox Pattern isn't flashy, but it removes one of the most painful reliability gaps in distributed systems. You stop hoping your database write and event publish both succeed, and instead make correctness part of the design.

If you're emitting integration events from an ASP.NET Core app, this pattern is usually the safest default.
