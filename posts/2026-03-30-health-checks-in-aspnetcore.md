---
title: Health Checks in ASP.NET Core
date: 2026-03-30
tags: dotnet, csharp, aspnetcore, healthchecks, monitoring
image: health-checks-in-aspnetcore.png
---

Yesterday we wired up OpenTelemetry to get traces, metrics, and logs flowing. Today we're looking at health checks — a simpler but equally important piece of the production-readiness puzzle. Where OTel tells you *how* your app is behaving, health checks tell orchestrators and load balancers whether your app is *ready to receive traffic* at all.

If you've ever had a Kubernetes pod marked `Running` while quietly broken — can't reach the database, out of memory, stuck in a bad state — you know why health checks matter. ASP.NET Core has a first-class health check API built in, and it takes about five minutes to get something useful running.

## Basic Setup

Health checks live in the `Microsoft.AspNetCore.Diagnostics.HealthChecks` package, which ships with the ASP.NET Core framework — no extra NuGet packages needed for the basics.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
```

Hit `/health` and you get a plain-text `Healthy` response with a 200 status. Not very exciting yet, but it's already useful as a basic liveness signal.

## Built-in Checks

### EF Core Database Check

If your app uses Entity Framework Core, add the EF Core health check package:

```bash
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
```

Then register it:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();
```

This runs a `SELECT 1` (or equivalent) against your database on every health check request. If the database is unreachable or the connection string is wrong, the check returns `Unhealthy`.

### Memory Check

The runtime ships a built-in memory check that fails when the process exceeds a threshold:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddCheck<MemoryHealthCheck>("memory");
```

Actually, for memory there's a simpler built-in approach — the `MemoryHealthCheck` isn't built in, but you can write one easily (more on that in the next section). For the EF Core check that's all you need.

## Custom Health Checks

Implement `IHealthCheck` and you can check anything — a downstream HTTP API, a message queue, a file on disk, a Redis connection:

```csharp
public class ExternalApiHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;

    public ExternalApiHealthCheck(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                "/ping", cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("API is reachable")
                : HealthCheckResult.Degraded($"API returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("API is unreachable", ex);
        }
    }
}
```

Register it:

```csharp
builder.Services.AddHttpClient<ExternalApiHealthCheck>(client =>
    client.BaseAddress = new Uri("https://api.example.com"));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddCheck<ExternalApiHealthCheck>("external-api");
```

`HealthCheckResult` has three states: `Healthy`, `Degraded`, and `Unhealthy`. Degraded means the app is working but something isn't quite right — useful for signalling partial degradation without pulling the instance out of rotation.

### Memory Check (Custom)

Here's that memory check — a common thing to want in production:

```csharp
public class MemoryHealthCheck : IHealthCheck
{
    private const long ThresholdBytes = 512 * 1024 * 1024; // 512 MB

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var allocated = GC.GetTotalMemory(forceFullCollection: false);
        var data = new Dictionary<string, object>
        {
            { "allocated_bytes", allocated },
            { "threshold_bytes", ThresholdBytes }
        };

        return Task.FromResult(allocated < ThresholdBytes
            ? HealthCheckResult.Healthy("Memory usage is within limits", data)
            : HealthCheckResult.Unhealthy("Memory usage exceeded threshold", data: data));
    }
}
```

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddCheck<ExternalApiHealthCheck>("external-api")
    .AddCheck<MemoryHealthCheck>("memory");
```

## Detailed JSON Responses

By default, `/health` just returns `Healthy` as plain text. That's fine for a basic ping, but you often want to know *which* checks passed and which failed. Use `HealthCheckOptions` to get JSON output:

```csharp
app.MapHealthChecks("/health/detail", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds,
                data = e.Value.Data,
                exception = e.Value.Exception?.Message
            })
        });

        await context.Response.WriteAsync(result);
    }
});
```

Hit `/health/detail` and you get something like:

```json
{
  "status": "Healthy",
  "duration": 45.2,
  "checks": [
    {
      "name": "AppDbContext",
      "status": "Healthy",
      "description": null,
      "duration": 32.1,
      "data": {}
    },
    {
      "name": "external-api",
      "status": "Healthy",
      "description": "API is reachable",
      "duration": 12.8,
      "data": {}
    },
    {
      "name": "memory",
      "status": "Healthy",
      "description": "Memory usage is within limits",
      "duration": 0.1,
      "data": {
        "allocated_bytes": 45678912,
        "threshold_bytes": 536870912
      }
    }
  ]
}
```

This endpoint is great for dashboards and on-call debugging. You can see at a glance which check is misbehaving and how long it's taking.

## Readiness vs Liveness Probes

If you're deploying to Kubernetes, you'll want separate endpoints for liveness and readiness. They mean different things:

- **Liveness**: Is the process alive and not deadlocked? If liveness fails, Kubernetes restarts the pod.
- **Readiness**: Is the app ready to serve traffic? If readiness fails, Kubernetes removes the pod from the load balancer but doesn't restart it.

The distinction matters. A database being temporarily unavailable should fail readiness (stop sending traffic) but not liveness (don't restart the app — the process itself is fine).

Tag your checks and filter by tag:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: new[] { "ready" })
    .AddCheck<ExternalApiHealthCheck>("external-api", tags: new[] { "ready" })
    .AddCheck<MemoryHealthCheck>("memory", tags: new[] { "live" });
```

Then expose two endpoints, each showing only the relevant checks:

```csharp
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

Your Kubernetes deployment then looks like this:

```yaml
livenessProbe:
  httpGet:
    path: /healthz/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 15

readinessProbe:
  httpGet:
    path: /healthz/ready
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 10
```

This is the pattern you'll see in most production .NET Kubernetes deployments. It's simple and it works well.

## Health Check UI

If you want a visual dashboard without building your own, the community `AspNetCore.HealthChecks.UI` package gives you one:

```bash
dotnet add package AspNetCore.HealthChecks.UI
dotnet add package AspNetCore.HealthChecks.UI.Client
dotnet add package AspNetCore.HealthChecks.UI.InMemory.Storage
```

Configure it:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: new[] { "ready" })
    .AddCheck<MemoryHealthCheck>("memory", tags: new[] { "live" });

builder.Services.AddHealthChecksUI(options =>
    {
        options.SetEvaluationTimeInSeconds(15);
        options.AddHealthCheckEndpoint("API", "/health/detail");
    })
    .AddInMemoryStorage();
```

Update the detail endpoint to use the UI-compatible writer:

```csharp
app.MapHealthChecks("/health/detail", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecksUI(options => options.UIPath = "/health-ui");
```

Navigate to `/health-ui` and you'll see a dashboard showing all your checks, their history, and any failures. It's particularly useful in staging environments where you want quick visibility without a full monitoring stack.

## Routing and Authorization

Health check endpoints are standard ASP.NET Core endpoints, so you can apply routing conventions and authorization policies to them like anything else:

```csharp
// Only expose the detailed endpoint internally
app.MapHealthChecks("/health/detail", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
})
.RequireAuthorization("InternalOnly");

// Keep the liveness/readiness probes open — Kubernetes needs them
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

A common mistake is putting an authorization requirement on the liveness/readiness probes. Kubernetes doesn't send auth headers, so they'll always fail. Keep those open and protect only the detailed diagnostic endpoints.

## Putting It All Together

Here's a complete `Program.cs` with everything wired up:

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpClient<ExternalApiHealthCheck>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["ExternalApi:BaseUrl"] ?? "https://api.example.com"));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: new[] { "ready" })
    .AddCheck<ExternalApiHealthCheck>("external-api", tags: new[] { "ready" })
    .AddCheck<MemoryHealthCheck>("memory", tags: new[] { "live" });

builder.Services.AddHealthChecksUI(options =>
    {
        options.SetEvaluationTimeInSeconds(15);
        options.AddHealthCheckEndpoint("API", "/health/detail");
    })
    .AddInMemoryStorage();

builder.Services.AddControllers();

var app = builder.Build();

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/detail", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecksUI(options => options.UIPath = "/health-ui");

app.MapControllers();
app.Run();
```

Three endpoints, two custom checks, a visual UI, and Kubernetes-ready liveness/readiness probes. The whole thing fits comfortably in `Program.cs` with almost no boilerplate.

Health checks are one of those things you don't notice until they're missing. Add them early — they're cheap to write and they'll save you from the 2 AM "why is traffic going to a broken pod" conversation. Combined with the OpenTelemetry setup from yesterday, you've now got a solid observability foundation: traces and metrics for understanding behaviour, and health checks so your orchestrator always knows which instances are fit to serve traffic.
