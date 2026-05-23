---
title: Optimistic Concurrency in EF Core
date: 2026-05-19
tags: dotnet, efcore, data, architecture
image: optimistic-concurrency-in-ef-core.png
---

Most business systems eventually hit a "last write wins" bug. Two users edit the same record, both click save, and the second write silently overwrites the first.

EF Core's optimistic concurrency support is the easiest way to stop that.

## Add a Concurrency Token

The standard approach is a `rowversion`/timestamp column:

```csharp
public sealed class Invoice
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
}

public sealed class AppDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>()
            .Property(x => x.Version)
            .IsRowVersion();
    }
}
```

EF Core includes `Version` in update predicates so the write only succeeds if the row is unchanged.

## Handle Conflicts Explicitly

When versions differ, `SaveChangesAsync()` throws `DbUpdateConcurrencyException`:

```csharp
try
{
    invoice.Status = "Paid";
    await db.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException)
{
    return Results.Conflict(new
    {
        error = "The invoice was updated by another user. Reload and try again."
    });
}
```

Now clients get a clear conflict instead of silent data loss.

## ETag-Friendly API Pattern

For APIs, expose concurrency via ETags and `If-Match`:

```csharp
app.MapPut("/invoices/{id:int}", async (
    int id,
    UpdateInvoiceRequest request,
    HttpRequest httpRequest,
    AppDbContext db,
    CancellationToken ct) =>
{
    var invoice = await db.Invoices.FindAsync([id], ct);
    if (invoice is null) return Results.NotFound();

    var ifMatch = httpRequest.Headers.IfMatch.ToString().Trim('"');
    var current = Convert.ToBase64String(invoice.Version);

    if (!string.Equals(ifMatch, current, StringComparison.Ordinal))
    {
        return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
    }

    invoice.Status = request.Status;
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
});
```

That pattern maps concurrency checks cleanly to HTTP semantics.

## Wrapping Up

Optimistic concurrency is one of those features that's easy to postpone and painful to skip. A single concurrency token plus explicit conflict handling protects real data and avoids awkward support tickets later.

If multiple users can edit the same records, turn this on early.
