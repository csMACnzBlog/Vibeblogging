---
title: Event Sourcing in .NET
date: 2026-04-19
tags: dotnet, csharp, architecture, tutorial
image: event-sourcing-in-dotnet.png
---

Yesterday we looked at CQRS, which separates reads from writes. Today's topic pairs naturally with it: event sourcing. Instead of storing the current state of your entities, you store the sequence of events that got you there.

It sounds like a big shift — and it is — but the core idea is straightforward once you see it in code.

## What Is Event Sourcing?

Traditional persistence stores *current state*. You have a `products` table, and when a price changes you update the row. The old price is gone.

Event sourcing stores *what happened*. You have an `events` table, and when a price changes you append a `ProductPriceChanged` event. The full history is always there.

That history is the superpower. You can reconstruct the current state at any point in time, build new projections from scratch, and create audit logs without any extra work — they're just the event stream.

## Modelling Events

Events are plain, immutable records of things that happened. Use records for them — they're value-typed and concise:

```csharp
public abstract record DomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

public record ProductCreated(Guid ProductId, string Name, decimal Price)
    : DomainEvent;

public record ProductPriceChanged(Guid ProductId, decimal OldPrice, decimal NewPrice)
    : DomainEvent;

public record ProductRenamed(Guid ProductId, string OldName, string NewName)
    : DomainEvent;
```

Events are named in past tense — they describe something that already happened.

## The Aggregate

An aggregate is the entity that raises events and rebuilds itself from them. It keeps a list of uncommitted events, and it applies each event to update its own state:

```csharp
public class Product
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public decimal Price { get; private set; }

    private readonly List<DomainEvent> _uncommittedEvents = new();
    public IReadOnlyList<DomainEvent> UncommittedEvents => _uncommittedEvents;

    // Parameterless constructor for rebuilding from events
    private Product() { }

    // Factory method — raises the creation event
    public static Product Create(string name, decimal price)
    {
        var product = new Product();
        product.Apply(new ProductCreated(Guid.NewGuid(), name, price));
        return product;
    }

    public void ChangePrice(decimal newPrice)
    {
        if (newPrice <= 0)
            throw new ArgumentException("Price must be positive.");

        Apply(new ProductPriceChanged(Id, Price, newPrice));
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Name cannot be empty.");

        Apply(new ProductRenamed(Id, Name, newName));
    }

    private void Apply(DomainEvent @event)
    {
        // Update state
        When(@event);
        // Track for persistence
        _uncommittedEvents.Add(@event);
    }

    private void When(DomainEvent @event)
    {
        switch (@event)
        {
            case ProductCreated e:
                Id = e.ProductId;
                Name = e.Name;
                Price = e.Price;
                break;
            case ProductPriceChanged e:
                Price = e.NewPrice;
                break;
            case ProductRenamed e:
                Name = e.NewName;
                break;
        }
    }

    // Rebuild from a stream of stored events
    public static Product Rehydrate(IEnumerable<DomainEvent> events)
    {
        var product = new Product();
        foreach (var @event in events)
            product.When(@event);
        return product;
    }

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();
}
```

The key is the separation between `Apply` (used for new events during a command) and `When` (used for both new events and replay). `Rehydrate` skips `_uncommittedEvents` because those events are already in the store.

## Storing Events

You need somewhere to persist the events. A simple in-memory event store makes it easy to understand the shape:

```csharp
public interface IEventStore
{
    Task AppendEventsAsync(Guid aggregateId, IEnumerable<DomainEvent> events);
    Task<IReadOnlyList<DomainEvent>> LoadEventsAsync(Guid aggregateId);
}

public class InMemoryEventStore : IEventStore
{
    private readonly Dictionary<Guid, List<DomainEvent>> _store = new();

    public Task AppendEventsAsync(Guid aggregateId, IEnumerable<DomainEvent> events)
    {
        if (!_store.TryGetValue(aggregateId, out var stream))
        {
            stream = new List<DomainEvent>();
            _store[aggregateId] = stream;
        }
        stream.AddRange(events);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DomainEvent>> LoadEventsAsync(Guid aggregateId)
    {
        if (!_store.TryGetValue(aggregateId, out var stream))
            return Task.FromResult<IReadOnlyList<DomainEvent>>(Array.Empty<DomainEvent>());

        return Task.FromResult<IReadOnlyList<DomainEvent>>(stream.AsReadOnly());
    }
}
```

In production you'd swap this for a real event store backed by PostgreSQL, SQL Server, or a dedicated tool like EventStoreDB. The interface stays the same.

## The Repository

The repository loads events, rebuilds the aggregate, and saves new events after a command:

```csharp
public class ProductRepository
{
    private readonly IEventStore _eventStore;

    public ProductRepository(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<Product?> GetByIdAsync(Guid id)
    {
        var events = await _eventStore.LoadEventsAsync(id);
        if (events.Count == 0)
            return null;

        return Product.Rehydrate(events);
    }

    public async Task SaveAsync(Product product)
    {
        var events = product.UncommittedEvents;
        if (events.Count == 0)
            return;

        await _eventStore.AppendEventsAsync(product.Id, events);
        product.ClearUncommittedEvents();
    }
}
```

Every time you load a product, you replay all its events to rebuild the current state. Every time you save, you only persist what's new.

## Putting It Together

A command handler using this pattern looks like any other:

```csharp
public record ChangePriceCommand(Guid ProductId, decimal NewPrice);

public class ChangePriceHandler
{
    private readonly ProductRepository _repository;

    public ChangePriceHandler(ProductRepository repository)
    {
        _repository = repository;
    }

    public async Task HandleAsync(ChangePriceCommand command)
    {
        var product = await _repository.GetByIdAsync(command.ProductId)
            ?? throw new InvalidOperationException("Product not found.");

        product.ChangePrice(command.NewPrice);

        await _repository.SaveAsync(product);
    }
}
```

Clean, simple, and completely testable — the aggregate has no dependency on infrastructure.

## Building Projections

The real payoff comes when you want to query data. You build *projections* by reading the event stream and building a read model:

```csharp
public class ProductReadModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public List<decimal> PriceHistory { get; set; } = new();
}

public class ProductProjection
{
    private readonly Dictionary<Guid, ProductReadModel> _readModels = new();

    public void Project(DomainEvent @event)
    {
        switch (@event)
        {
            case ProductCreated e:
                _readModels[e.ProductId] = new ProductReadModel
                {
                    Id = e.ProductId,
                    Name = e.Name,
                    Price = e.Price,
                    PriceHistory = [e.Price]
                };
                break;
            case ProductPriceChanged e:
                if (_readModels.TryGetValue(e.ProductId, out var model))
                {
                    model.Price = e.NewPrice;
                    model.PriceHistory.Add(e.NewPrice);
                }
                break;
            case ProductRenamed e:
                if (_readModels.TryGetValue(e.ProductId, out var renamed))
                    renamed.Name = e.NewName;
                break;
        }
    }

    public ProductReadModel? Get(Guid id) =>
        _readModels.TryGetValue(id, out var m) ? m : null;
}
```

This projection tracks price history — something you get for free because all the data is already in your event stream. With a traditional approach you'd need an extra audit table and extra code to populate it.

## Testing Is a Pleasure

Aggregates are pure objects. No database, no dependencies — just events in, events out:

```csharp
public class ProductTests
{
    [Fact]
    public void Create_raises_ProductCreated_event()
    {
        var product = Product.Create("Widget", 9.99m);

        var @event = Assert.Single(product.UncommittedEvents);
        var created = Assert.IsType<ProductCreated>(@event);
        Assert.Equal("Widget", created.Name);
        Assert.Equal(9.99m, created.Price);
    }

    [Fact]
    public void ChangePrice_raises_ProductPriceChanged_event()
    {
        var product = Product.Create("Widget", 9.99m);
        product.ClearUncommittedEvents();

        product.ChangePrice(12.99m);

        var @event = Assert.Single(product.UncommittedEvents);
        var changed = Assert.IsType<ProductPriceChanged>(@event);
        Assert.Equal(9.99m, changed.OldPrice);
        Assert.Equal(12.99m, changed.NewPrice);
    }

    [Fact]
    public void Rehydrate_restores_state_from_events()
    {
        var events = new DomainEvent[]
        {
            new ProductCreated(Guid.NewGuid(), "Widget", 9.99m),
            new ProductPriceChanged(Guid.Empty, 9.99m, 14.99m),
        };

        var product = Product.Rehydrate(events);

        Assert.Equal(14.99m, product.Price);
    }
}
```

No stubs. No mocks. No setup. Just instantiate and assert.

## When to Reach for Event Sourcing

Event sourcing shines when you need:

- **Audit trails** — the event stream is your audit log by default
- **Temporal queries** — "what did this order look like on Tuesday?" is just replaying to a point in time
- **Multiple projections** — the same events feed a dashboard, a search index, and a reporting database
- **Debugging production issues** — replay the exact sequence of events that caused a bug

It's not the right choice for everything. Simple CRUD apps, especially those that don't need history, pay a complexity tax without much benefit. Start simple and reach for event sourcing when the domain demands it.

## Wrapping Up

Event sourcing is a powerful pattern and a natural companion to CQRS. Your write side produces events, your read side consumes them to build projections, and your event store is the single source of truth.

The aggregate pattern keeps all the complexity in one place: events come in via `Apply`, state changes happen via `When`, and you get full replayability for free. Start with the in-memory event store, get comfortable with the pattern, then swap in a persistent store when you're ready.
