---
title: Request Timeout Middleware in ASP.NET Core
date: 2026-05-21
tags: aspnetcore, dotnet, middleware, performance
image: request-timeout-middleware-in-aspnetcore.png
---

Long-running requests are rough on everyone. Users wait, reverse proxies retry, and thread pool pressure quietly ramps up.

ASP.NET Core's request timeout middleware gives you a clear upper bound so slow paths fail fast and predictably.

## Enable Request Timeouts

```csharp
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(10),
        TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable
    };

    options.AddPolicy("long-running", TimeSpan.FromSeconds(30));
});

var app = builder.Build();
app.UseRequestTimeouts();
```

Now requests that exceed policy limits are cancelled and return the configured status code.

## Apply Per Endpoint

```csharp
app.MapGet("/reports/summary", async (IReportService reports, CancellationToken ct) =>
{
    var model = await reports.BuildSummaryAsync(ct);
    return Results.Ok(model);
});

app.MapPost("/reports/rebuild", async (IReportService reports, CancellationToken ct) =>
{
    await reports.RebuildSnapshotsAsync(ct);
    return Results.Accepted();
})
.WithRequestTimeout("long-running");
```

You can keep aggressive defaults and only relax limits where you intentionally expect longer work.

## CancellationToken Matters

Timeout middleware is only effective if downstream code respects cancellation:

```csharp
public async Task<SummaryDto> BuildSummaryAsync(CancellationToken ct)
{
    var rows = await _db.Orders
        .AsNoTracking()
        .Where(x => x.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30))
        .ToListAsync(ct);

    ct.ThrowIfCancellationRequested();
    return Summarise(rows);
}
```

If services ignore the token, the app still burns resources after the client has already given up.

## Wrapping Up

Request timeout middleware is a practical guardrail. It protects capacity, improves predictability, and gives clients a fast failure signal instead of hanging forever.

If you haven't set explicit request time budgets yet, this is an easy reliability improvement.
