---
title: Idempotency Keys in ASP.NET Core APIs
date: 2026-05-12
tags: aspnetcore, dotnet, api, reliability, architecture
image: idempotency-keys-in-aspnetcore-apis.png
---

If you've ever had a client retry a `POST` after a timeout, you already know the pain: one user action can create two orders, two payments, or two emails. The retry was valid, but the side effect happened twice.

Idempotency keys fix that at the API boundary. The client sends a unique key per intended operation, and your API guarantees that repeating the same key won't repeat the write.

## Why Retries Create Duplicate Writes

Imagine a client calls `POST /orders`, your server writes to the database, but the response gets lost. The client retries because it never saw success. Without protection, the retry creates a second order.

You can reduce retries with better network conditions, but you can't remove them from real systems. So the safer approach is to design the endpoint so duplicate submissions are harmless.

## Step 1: Require an Idempotency Key

A common approach is to require an `Idempotency-Key` header for write endpoints:

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request,
    HttpContext http,
    IOrderService orderService,
    CancellationToken ct) =>
{
    if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues) ||
        string.IsNullOrWhiteSpace(keyValues))
    {
        return Results.BadRequest("Idempotency-Key header is required.");
    }

    var key = keyValues.ToString().Trim();
    var result = await orderService.CreateOrderAsync(request, key, ct);

    return result.ToHttpResult();
});
```

Treat this key as part of the contract, not an optional enhancement.

## Step 2: Store Request State and Replay the First Result

When a request arrives, check whether the key already exists:

- If not, create a pending record, run the operation, and store the final response.
- If yes and already completed, replay the saved response.
- If yes but still processing, return a conflict (or a retry hint).

Here's a compact service implementation:

```csharp
public sealed class OrderService : IOrderService
{
    private readonly AppDbContext _db;

    public OrderService(AppDbContext db) => _db = db;

    public async Task<CreateOrderResult> CreateOrderAsync(
        CreateOrderRequest request,
        string idempotencyKey,
        CancellationToken ct)
    {
        var existing = await _db.IdempotencyRecords
            .SingleOrDefaultAsync(x => x.Key == idempotencyKey, ct);

        if (existing is { Status: IdempotencyStatus.Completed })
        {
            return CreateOrderResult.Replayed(existing.OrderId!.Value);
        }

        if (existing is { Status: IdempotencyStatus.Processing })
        {
            return CreateOrderResult.Conflict("Request with this key is already running.");
        }

        var record = new IdempotencyRecord
        {
            Key = idempotencyKey,
            Status = IdempotencyStatus.Processing,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.IdempotencyRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        var order = new Order(request.CustomerId, request.Total);
        _db.Orders.Add(order);

        record.Status = IdempotencyStatus.Completed;
        record.OrderId = order.Id;
        record.CompletedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return CreateOrderResult.Created(order.Id);
    }
}
```

The important bit is that the same key always converges to the same outcome.

## Step 3: Add TTL and Payload Validation

In production, you'll want two extra rules:

1. **Expiration window** (for example 24 hours) so the table/cache doesn't grow forever.
2. **Payload hash check** so the same key can't be reused for a different request body.

```csharp
public static string ComputePayloadHash(CreateOrderRequest request)
{
    var json = JsonSerializer.Serialize(request);
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
    return Convert.ToHexString(bytes);
}

if (existing is not null && existing.PayloadHash != ComputePayloadHash(request))
{
    return CreateOrderResult.Conflict("Idempotency key was reused with different payload.");
}
```

That second check prevents subtle bugs where clients accidentally recycle keys.

## Testing the Retry Path

At minimum, add a test that calls the same endpoint twice with the same idempotency key and verifies only one row is inserted.

```csharp
[Fact]
public async Task SameKey_DoesNotCreateDuplicateOrder()
{
    var key = Guid.NewGuid().ToString("N");

    var first = await _client.PostAsJsonAsync(
        "/orders",
        new CreateOrderRequest("cust-123", 49.99m),
        headers: h => h.Add("Idempotency-Key", key));

    var second = await _client.PostAsJsonAsync(
        "/orders",
        new CreateOrderRequest("cust-123", 49.99m),
        headers: h => h.Add("Idempotency-Key", key));

    Assert.Equal(HttpStatusCode.Created, first.StatusCode);
    Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    Assert.Equal(1, await _db.Orders.CountAsync());
}
```

Once this test passes, your retry behavior stops being "best effort" and becomes an explicit guarantee.

## Wrapping Up

If the Outbox Pattern protects your messaging boundary, idempotency keys protect your HTTP boundary. Together they remove a big chunk of "worked in dev, duplicated in prod" surprises.

If you're building write-heavy APIs in ASP.NET Core, this is one of those patterns that's worth implementing before you need it. You'll sleep better the first time a client starts retrying aggressively.
