---
title: Structured Logging in .NET with Serilog
date: 2026-03-28
tags: dotnet, csharp, logging, serilog, observability
image: structured-logging-with-serilog.png
---

Logging is one of those things every app does, but most apps do badly. You end up with files full of sentences like `"User 42 placed order 99 for $19.99"` — which looks fine until you need to query across a million log lines. That's where structured logging changes the game.

Instead of formatting data into a string, structured logging keeps each piece of data as a named property alongside the message. The text stays human-readable in development, but downstream sinks — files, databases, Elasticsearch, Seq — receive rich, queryable JSON. Serilog is the most popular structured logging library in the .NET ecosystem and it integrates cleanly with ASP.NET Core's `ILogger` abstraction.

## Getting Started

Add Serilog to your project:

```bash
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
```

Then configure it in `Program.cs`:

```csharp
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    var app = builder.Build();
    app.MapGet("/", () => "Hello, Serilog!");
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
```

The `try/finally` pattern ensures the log is fully flushed before the process exits — important for async sinks that buffer writes.

## Structured vs Plain Logging

Here's the key difference. With plain string interpolation, you lose the data:

```csharp
// ❌ Plain string — data is baked into the message, unqueryable
_logger.LogInformation($"Order {orderId} placed by user {userId} for ${amount}");

// ✅ Structured — each value is a named property
_logger.LogInformation(
    "Order {OrderId} placed by user {UserId} for {Amount:C}",
    orderId, userId, amount);
```

Both produce the same human-readable output in the console. But with the structured version, Serilog attaches `OrderId`, `UserId`, and `Amount` as first-class properties on the log event. In Seq or Elasticsearch you can filter with `OrderId = 99` just like a database query.

## Enrichers

Enrichers automatically attach properties to every log event. They're great for things like the machine name, environment, or request correlation IDs.

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} " +
        "{Properties:j}{NewLine}{Exception}")
    .CreateLogger();
```

`Enrich.FromLogContext()` is the most important one. It enables `LogContext.PushProperty`, which lets you attach properties for the lifetime of a scope:

```csharp
using (LogContext.PushProperty("RequestId", httpContext.TraceIdentifier))
using (LogContext.PushProperty("UserId", currentUser.Id))
{
    _logger.LogInformation("Processing checkout");
    await _orderService.PlaceOrderAsync(cart);
    _logger.LogInformation("Checkout complete");
}
```

Every log event inside the `using` block automatically carries `RequestId` and `UserId`. When you're tracing a bug across a distributed system, this is invaluable.

## Logging with ILogger (Dependency Injection)

In real applications you should inject `ILogger<T>` rather than using the static `Log` class directly. Serilog hooks into ASP.NET Core's logging infrastructure, so your controllers and services stay clean:

```csharp
public class OrdersController : ControllerBase
{
    private readonly ILogger<OrdersController> _logger;
    private readonly IOrderService _orderService;

    public OrdersController(
        ILogger<OrdersController> logger,
        IOrderService orderService)
    {
        _logger = logger;
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<IActionResult> PlaceOrder(OrderRequest request)
    {
        _logger.LogInformation(
            "Placing order for {ProductId} x{Quantity}",
            request.ProductId, request.Quantity);

        var order = await _orderService.CreateAsync(request);

        _logger.LogInformation(
            "Order {OrderId} created successfully", order.Id);

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }
}
```

Serilog intercepts these calls and forwards them through its pipeline. The `T` in `ILogger<T>` becomes the `SourceContext` property on the log event, so you can filter by class in your sink.

## Minimum Level Overrides

You don't always want `Debug` level noise from the entire application. Serilog lets you set different minimum levels per namespace:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("MyApp.Orders", LogEventLevel.Debug)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
```

This is a common production pattern: suppress the chatty ASP.NET Core infrastructure logs, but keep `Debug` on your own namespaces so you can see what your code is doing without drowning in framework noise.

## Sinks: Writing Logs Somewhere Useful

The console and file sinks are fine for development, but in production you want something queryable. A few popular options:

**Seq** (great for local dev and small teams):
```bash
dotnet add package Serilog.Sinks.Seq
```
```csharp
.WriteTo.Seq("http://localhost:5341")
```

**Elasticsearch**:
```bash
dotnet add package Serilog.Sinks.Elasticsearch
```
```csharp
.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(
    new Uri("http://localhost:9200"))
{
    AutoRegisterTemplate = true,
    IndexFormat = "myapp-logs-{0:yyyy.MM}"
})
```

**Application Insights** (if you're on Azure):
```bash
dotnet add package Serilog.Sinks.ApplicationInsights
```
```csharp
.WriteTo.ApplicationInsights(
    TelemetryConfiguration.Active,
    TelemetryConverter.Traces)
```

You can combine multiple sinks — Serilog fans out each log event to all of them simultaneously.

## Reading Configuration from appsettings.json

Hard-coding the logger configuration is fine for a demo, but in production you want to change log levels without redeploying. Serilog can read its config from `appsettings.json`:

```bash
dotnet add package Serilog.Settings.Configuration
```

```csharp
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();
```

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/app.log",
          "rollingInterval": "Day"
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName"]
  }
}
```

Now ops can crank up `Debug` for a specific namespace at runtime by updating the config and restarting — no code change needed.

## Destructuring Objects

Sometimes you want to log a whole object as structured data. Serilog's `@` destructuring operator does this:

```csharp
var order = new Order(id: 42, productId: "SKU-99", quantity: 3);

// Logs as a structured object, not just ToString()
_logger.LogInformation("Order created: {@Order}", order);
```

This emits all properties of `Order` as nested fields on the log event. In Seq you can click into the order and filter by `Order.ProductId = "SKU-99"`. Be careful with large objects or those containing sensitive data — use `[LogMasked]` from `Destructurama.Attributed` to hide fields like passwords or credit card numbers.

## Putting It All Together

Here's a minimal but production-ready setup that combines everything above:

```csharp
// Program.cs
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddControllers();

    var app = builder.Build();
    app.UseSerilogRequestLogging(); // Replaces the default request logs
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

`CreateBootstrapLogger()` gives you logging before the DI container is built — useful for catching startup failures. `UseSerilogRequestLogging()` replaces ASP.NET Core's verbose per-request middleware logs with a single structured line per request that includes method, path, status code, and elapsed time.

Structured logging pairs naturally with the distributed tracing that OpenTelemetry provides — which we'll dig into tomorrow. Once your logs carry correlation IDs and trace context, tying a slow request back to the specific log lines that produced it becomes straightforward.
