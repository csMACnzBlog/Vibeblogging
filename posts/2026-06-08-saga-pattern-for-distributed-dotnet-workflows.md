---
title: Saga Pattern for Distributed .NET Workflows
date: 2026-06-08
tags: csharp, dotnet, microservices, architecture
image: saga-pattern-for-distributed-dotnet-workflows.png
---

If you've built microservices for more than a week, you've probably hit this moment: one step succeeds, the next fails, and now your system is half-updated.

In a monolith, you'd wrap the whole thing in a database transaction. Across services, that usually isn't an option. That's where the Saga pattern helps. Instead of one giant transaction, you run a sequence of local transactions with compensating actions when things go wrong.

## Why a saga is different from a normal transaction

A distributed workflow like "place order" often touches multiple services:

1. Reserve inventory
2. Authorize payment
3. Create shipment

Each service owns its own database, so you can't just open one SQL transaction and commit at the end. A saga handles this by:

- Running each step in order
- Persisting progress
- Triggering compensations for already-completed steps if a later step fails

Think "undo in reverse order", not "rollback with a shared lock".

## A simple saga contract

Let's start with a tiny contract for saga steps:

```csharp
using System.Threading;
using System.Threading.Tasks;

public interface ISagaStep<TContext>
{
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken);
    Task CompensateAsync(TContext context, CancellationToken cancellationToken);
}
```

Each step knows how to do work and how to undo it. The context carries IDs and state between steps.

## A practical order workflow

Here's a stripped-down orchestrator that runs steps and compensates on failure:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class SagaOrchestrator<TContext>
{
    private readonly IReadOnlyList<ISagaStep<TContext>> _steps;

    public SagaOrchestrator(IReadOnlyList<ISagaStep<TContext>> steps)
    {
        _steps = steps;
    }

    public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
    {
        var completed = new Stack<ISagaStep<TContext>>();

        try
        {
            foreach (var step in _steps)
            {
                await step.ExecuteAsync(context, cancellationToken);
                completed.Push(step);
            }
        }
        catch
        {
            while (completed.Count > 0)
            {
                var completedStep = completed.Pop();
                await completedStep.CompensateAsync(context, cancellationToken);
            }

            throw;
        }
    }
}
```

This isn't production-complete yet, but it's the core idea you'll keep even in more advanced implementations.

## Example step: reserve then release inventory

```csharp
using System.Threading;
using System.Threading.Tasks;

public sealed record OrderSagaContext(string OrderId, string ProductId, int Quantity, decimal Amount)
{
    public string? ReservationId { get; set; }
    public string? PaymentAuthorizationId { get; set; }
}

public interface IInventoryClient
{
    Task<string> ReserveAsync(string productId, int quantity, CancellationToken cancellationToken);
    Task ReleaseAsync(string reservationId, CancellationToken cancellationToken);
}

public sealed class ReserveInventoryStep : ISagaStep<OrderSagaContext>
{
    private readonly IInventoryClient _inventoryClient;

    public ReserveInventoryStep(IInventoryClient inventoryClient)
    {
        _inventoryClient = inventoryClient;
    }

    public async Task ExecuteAsync(OrderSagaContext context, CancellationToken cancellationToken)
    {
        context.ReservationId = await _inventoryClient.ReserveAsync(
            context.ProductId,
            context.Quantity,
            cancellationToken);
    }

    public async Task CompensateAsync(OrderSagaContext context, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(context.ReservationId))
        {
            await _inventoryClient.ReleaseAsync(context.ReservationId, cancellationToken);
        }
    }
}
```

The compensation method is idempotent-friendly: if there's no reservation ID, it safely does nothing.

## The two mistakes teams make first

When sagas feel flaky in production, it's usually one of these:

### 1) No persistent saga state

If the orchestrator crashes mid-workflow, you need durable state to resume or compensate later. Keep a saga instance record with:

- Correlation ID
- Current step index
- Status (`Running`, `Completed`, `Compensating`, `Failed`)
- Last error

### 2) Non-idempotent compensations

Compensation might run more than once due to retries or timeouts. Design each compensation endpoint so duplicate calls are safe.

For example, "release reservation" should be okay even if the reservation was already released.

## Where this fits with outbox and messaging

Sagas don't replace the outbox pattern; they complement it.

- Use outbox to reliably publish integration events from local transactions.
- Use saga orchestration/choreography to coordinate multi-step business workflows.

If you're already using idempotency keys and an outbox (great choices), sagas are the missing coordination layer.

## Final thought

A saga won't make distributed systems simple, but it *will* make failures survivable and predictable.

Start with one concrete workflow, model explicit compensations, and persist saga state from day one. You'll still have failures (of course), but you won't have mystery partial updates that take hours to untangle.
