---
title: Getting Started with OpenTelemetry in ASP.NET Core
date: 2026-03-29
tags: dotnet, csharp, opentelemetry, observability, tracing
image: opentelemetry-in-aspnetcore.png
---

Yesterday we added structured logging with Serilog and talked about correlation IDs. Today we're going to complete the picture with OpenTelemetry — the open standard that ties traces, metrics, and logs together into a single coherent view of what your application is actually doing.

If you've ever stared at logs trying to reconstruct why a request took four seconds, you know the pain. OpenTelemetry gives you distributed traces so you can see every span of work across every service — and correlate those spans back to the log lines that produced them.

## What is OpenTelemetry?

OpenTelemetry (OTel) is a vendor-neutral observability framework. It's a CNCF project that defines a standard API and SDK for capturing telemetry data — traces, metrics, and logs — and exporting it to whatever backend you prefer (Jaeger, Zipkin, Prometheus, Grafana, Azure Monitor, and dozens more).

The appeal in .NET is that OTel hooks into the platform instrumentation you already have. ASP.NET Core, HttpClient, EF Core, and SQL all emit telemetry out of the box — you just need to wire OTel up to collect it.

## The Three Pillars

Before we get into code, it's worth being clear on what each pillar does:

- **Traces** — a trace represents a single request's journey through your system. It's made up of *spans*, each representing a unit of work (handling an HTTP request, querying the database, calling a downstream API). Spans are nested and linked, so you can see the full call tree.
- **Metrics** — aggregated numeric measurements over time: request counts, error rates, latency histograms, resource usage. Great for dashboards and alerting.
- **Logs** — structured log events. With OTel, logs can carry trace and span IDs so they're automatically correlated with the right trace.

## Getting Started

Add the core packages:

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Exporter.Console
```

Then configure OTel in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();
app.MapGet("/", () => "Hello, OpenTelemetry!");
app.Run();
```

Run the app and hit an endpoint — you'll see trace and metric data printed to the console. It's verbose, but it's a great way to confirm everything is wired up before you point it at a real backend.

## Adding EF Core Instrumentation

If you're using Entity Framework Core, add the EF Core package and it'll automatically trace every query:

```bash
dotnet add package OpenTelemetry.Instrumentation.EntityFrameworkCore
```

```csharp
.WithTracing(tracing => tracing
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddEntityFrameworkCoreInstrumentation()
    .AddConsoleExporter())
```

Now each trace includes database spans with the SQL statement, duration, and whether it hit an error. When you're debugging a slow endpoint, seeing `SELECT * FROM Orders WHERE ...` taking 800 ms in the trace is far more useful than a generic "request was slow" log line.

## Exporting to Jaeger or Zipkin

The console exporter is for development. In a real environment you want to export to a trace collector. The most common choice for local dev is Jaeger — it's free, open source, and ships a Docker image with a built-in UI.

Start Jaeger with Docker:

```bash
docker run -d --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  -p 4318:4318 \
  jaegertracing/all-in-one:latest
```

Then switch to the OTLP exporter (which speaks the OpenTelemetry Protocol that Jaeger understands):

```bash
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter(otlp =>
        {
            otlp.Endpoint = new Uri("http://localhost:4317");
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(otlp =>
        {
            otlp.Endpoint = new Uri("http://localhost:4317");
        }));
```

Open `http://localhost:16686` and you'll see your service in the Jaeger UI. Each request shows up as a trace with spans for ASP.NET Core routing, your controller, database queries, and any outbound HTTP calls.

Zipkin is an alternative to Jaeger — the setup is almost identical, just use `OpenTelemetry.Exporter.Zipkin` and point it at `http://localhost:9411`.

## Custom Spans and Activities

Auto-instrumentation covers the infrastructure, but you'll often want to trace your own business logic. OTel in .NET uses `System.Diagnostics.ActivitySource` — it's the native .NET API that OTel wraps.

```csharp
public class OrderService
{
    private static readonly ActivitySource ActivitySource =
        new("MyApp.Orders");

    private readonly AppDbContext _db;

    public OrderService(AppDbContext db) => _db = db;

    public async Task<Order> CreateAsync(OrderRequest request)
    {
        using var activity = ActivitySource.StartActivity("CreateOrder");
        activity?.SetTag("order.product_id", request.ProductId);
        activity?.SetTag("order.quantity", request.Quantity);

        var order = new Order(request.ProductId, request.Quantity);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        activity?.SetTag("order.id", order.Id);
        return order;
    }
}
```

Register the source with OTel so it's picked up:

```csharp
.WithTracing(tracing => tracing
    .AddSource("MyApp.Orders")  // <-- your custom source
    .AddAspNetCoreInstrumentation()
    // ...
```

Now `CreateOrder` appears as a child span inside the ASP.NET Core span for the request. You can see exactly how long the business logic takes versus the database call.

## Custom Metrics

OTel metrics use the `System.Diagnostics.Metrics` API, also part of the .NET runtime:

```csharp
public class OrderService
{
    private static readonly Meter Meter = new("MyApp.Orders");
    private static readonly Counter<int> OrdersPlaced =
        Meter.CreateCounter<int>("orders.placed");
    private static readonly Histogram<double> OrderValue =
        Meter.CreateHistogram<double>("orders.value", unit: "USD");

    public async Task<Order> CreateAsync(OrderRequest request)
    {
        // ... create order ...

        OrdersPlaced.Add(1, new TagList
        {
            { "product_id", request.ProductId }
        });
        OrderValue.Record(order.TotalAmount);

        return order;
    }
}
```

Register the meter:

```csharp
.WithMetrics(metrics => metrics
    .AddMeter("MyApp.Orders")
    .AddAspNetCoreInstrumentation()
    .AddOtlpExporter())
```

These metrics flow to your backend alongside the standard ASP.NET Core metrics. In Grafana you can build a dashboard showing orders per minute, broken down by product — all from the tags you attached.

## Correlating Logs with Traces

Here's where yesterday's Serilog work pays off. When OTel is active, the current `Activity` (the active trace span) is accessible from log enrichers. Serilog can attach the trace ID and span ID to every log event automatically.

```bash
dotnet add package Serilog.Enrichers.Span
```

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()            // adds TraceId and SpanId
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] " +
        "[{TraceId}:{SpanId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
```

Now every log line carries the trace ID from the current OTel span. In Seq or Elasticsearch, you can search by trace ID and immediately see all the logs for a specific request — even across multiple services if they propagate the W3C `traceparent` header.

If you export logs to an OTel-compatible backend via `OpenTelemetry.Logs`, the correlation is automatic and the logs appear directly inside the Jaeger trace view alongside the spans.

## Reading Config from appsettings.json

Hard-coding exporter endpoints and sampling rates isn't great. The standard pattern is to drive OTel config from `appsettings.json`:

```json
{
  "OpenTelemetry": {
    "ServiceName": "my-api",
    "Endpoint": "http://localhost:4317"
  }
}
```

```csharp
var serviceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "unknown";
var endpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(otlp =>
            otlp.Endpoint = new Uri(endpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(otlp =>
            otlp.Endpoint = new Uri(endpoint)));
```

`ConfigureResource` sets the service name that shows up in Jaeger's service list and Grafana dashboards. Set it per environment — `my-api-prod`, `my-api-staging` — so you can filter traces to a specific deployment.

## Putting It All Together

Here's a complete minimal setup that combines everything:

```csharp
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithSpan());

    var serviceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "my-api";
    var otlpEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("MyApp.*")
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("MyApp.*")
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

    builder.Services.AddControllers();

    var app = builder.Build();
    app.UseSerilogRequestLogging();
    app.MapControllers();
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
```

With this setup you get: structured logs with trace correlation, distributed traces in Jaeger, runtime and HTTP metrics flowing to your backend, and custom spans and counters for your business logic. The whole observability stack in under 50 lines of configuration.

OpenTelemetry is one of those investments that pays off the first time a production incident lands in your lap — instead of grepping through log files, you open Jaeger, find the slow trace, and see exactly which database query or downstream service caused the problem. Give it a try in your next project.
