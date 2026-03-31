---
title: Background Services in ASP.NET Core
date: 2026-03-31
tags: dotnet, csharp, aspnetcore, backgroundservices, hosting
image: background-services-in-aspnetcore.png
---

Think about a restaurant kitchen. The wait staff handle the incoming orders — they're your request-handling pipeline, busy and visible. But behind the scenes, someone's doing prep work, cleaning equipment, taking stock of the pantry. That's work that doesn't map to any one customer request. It just needs to happen, reliably, in the background.

ASP.NET Core has a first-class answer for that kind of work: hosted services. They run alongside your application, start when the host starts, and stop when the host stops. No hacky timer hacks, no fire-and-forget tasks escaping into the void.

## IHostedService

Everything starts with `IHostedService`. It's a two-method interface:

```csharp
public interface IHostedService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

`StartAsync` is called when the host starts, `StopAsync` when it's shutting down. Simple. You could implement it directly, but you almost never want to — because if you do work *inside* `StartAsync`, you'll block the host from finishing its startup.

The pattern is to kick off a long-running task and return immediately:

```csharp
public class MyHostedService : IHostedService
{
    private Task? _backgroundTask;
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _backgroundTask = DoWorkAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task DoWorkAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // do something useful
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_backgroundTask is not null)
            await Task.WhenAny(_backgroundTask, Task.Delay(Timeout.Infinite, cancellationToken));
    }
}
```

That works, but it's a lot of plumbing for what should be a simple thing. Which is why `BackgroundService` exists.

## BackgroundService

`BackgroundService` is an abstract base class in `Microsoft.Extensions.Hosting` that handles all of that for you. Override one method:

```csharp
public class MyBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // do something useful
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
```

That's it. `BackgroundService` handles starting a background task, passing a cancellation token, and waiting for it to complete on shutdown. You focus on what your service actually does.

Register it in `Program.cs`:

```csharp
builder.Services.AddHostedService<MyBackgroundService>();
```

## Periodic Work with PeriodicTimer

The `Task.Delay` loop works, but there's a subtlety: if your work takes longer than the delay, you'll start overlapping executions (or at least have a shorter gap than intended, depending on how you write it). And `Task.Delay` doesn't give you quite the same regular heartbeat you might want.

.NET 6 added `PeriodicTimer` — a timer designed specifically for this pattern:

```csharp
public class DataSyncService : BackgroundService
{
    private readonly ILogger<DataSyncService> _logger;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMinutes(5));

    public DataSyncService(ILogger<DataSyncService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Syncing data at {Time}", DateTimeOffset.UtcNow);
                await SyncDataAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during data sync");
            }
        }
    }

    private async Task SyncDataAsync(CancellationToken cancellationToken)
    {
        // your actual sync logic
        await Task.Delay(100, cancellationToken);
    }

    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }
}
```

`WaitForNextTickAsync` returns `false` when the cancellation token is triggered, so the `while` loop exits cleanly. No need to check `stoppingToken.IsCancellationRequested` manually. Neat.

The try/catch around the work is important. If an unhandled exception escapes `ExecuteAsync`, `BackgroundService` catches it and stops the service — it won't restart automatically. Catching non-cancellation exceptions inside the loop lets your service recover from transient failures instead of silently dying.

## Scoped Services Inside a Hosted Service

Here's the gotcha that trips people up. Hosted services are singletons — they live for the lifetime of the application. But most of your services (like `DbContext`) are scoped — they're meant to live for one request.

You can't just inject a scoped service into a singleton. (Well, you can, but you'll get a runtime exception or, worse, incorrect behaviour when the scoped service is resolved once and held forever.) Instead, use `IServiceScopeFactory`:

```csharp
public class ReportGeneratorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportGeneratorService> _logger;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromHours(1));

    public ReportGeneratorService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReportGeneratorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();

            try
            {
                await reportService.GenerateDailyReportAsync(stoppingToken);
                _logger.LogInformation("Daily report generated successfully");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to generate daily report");
            }
        }
    }
}
```

Create a new scope for each unit of work, get your scoped services from it, and dispose the scope when you're done. The `await using` ensures `DisposeAsync` is called — important for `DbContext`, which has async disposal.

I've seen people inject `IServiceProvider` directly instead of `IServiceScopeFactory`. That works, but it's less explicit about what you're doing. `IServiceScopeFactory` makes the intent obvious: you know you're creating a scope, not grabbing from the root container.

## Graceful Shutdown

`BackgroundService.StopAsync` gives your service time to finish whatever it's doing before the process exits. By default, the host waits up to 30 seconds (configurable) for all hosted services to stop.

The cancellation token passed to `ExecuteAsync` will be cancelled when the host starts shutting down. If your work is well-behaved — passing the token to `await` calls and checking it in loops — shutdown will be clean.

Sometimes you need more time. If you're in the middle of a long operation and you really don't want it interrupted, you can configure the shutdown timeout:

```csharp
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
});
```

But be careful — keeping the process alive longer means deployments and restarts take longer too. It's a trade-off. I'd rather design my work to be idempotent and restartable than to push the timeout up and pretend the problem doesn't exist.

## Worker Services

If your background processing doesn't need to live inside a web app, .NET has a Worker Service project template for exactly this scenario — a console app built on the generic host:

```bash
dotnet new worker -n MyWorkerService
```

This gives you a `BackgroundService` subclass inside a minimal host, with no HTTP pipeline at all. Great for queue processors, scheduled jobs, or anything that runs in the background without serving web requests.

You get all the same DI, logging, configuration, and cancellation infrastructure as an ASP.NET Core app. Deploying as a Windows Service or systemd service is just one NuGet package away:

```bash
# Windows Service
dotnet add package Microsoft.Extensions.Hosting.WindowsServices

# Linux systemd
dotnet add package Microsoft.Extensions.Hosting.Systemd
```

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

// Uncomment for Windows Service or systemd
// builder.Services.AddWindowsService();
// builder.Services.AddSystemd();

var host = builder.Build();
host.Run();
```

Same code, different deployment target. I like that.

## Putting It All Together

Here's a realistic background service that polls a queue, processes messages with a scoped `DbContext`, and handles shutdown gracefully:

```csharp
public class MessageProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageProcessorService> _logger;
    private readonly IMessageQueue _queue;

    public MessageProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<MessageProcessorService> logger,
        IMessageQueue queue)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _queue = queue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Message processor starting");

        await foreach (var message in _queue.ReadAllAsync(stoppingToken))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<IMessageHandler>();

            try
            {
                await handler.HandleAsync(message, stoppingToken);
                await _queue.AcknowledgeAsync(message, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await _queue.NackAsync(message, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                await _queue.NackAsync(message, stoppingToken);
            }
        }

        _logger.LogInformation("Message processor stopped");
    }
}
```

The `await foreach` loop over the queue means we're only running when there's work to do — no polling delay, no spin loop. When the cancellation token fires, `ReadAllAsync` completes the `IAsyncEnumerable` and the loop exits naturally. Clean shutdown.

Register everything in `Program.cs`:

```csharp
builder.Services.AddSingleton<IMessageQueue, RedisMessageQueue>();
builder.Services.AddScoped<IMessageHandler, OrderMessageHandler>();
builder.Services.AddHostedService<MessageProcessorService>();
```

Background services are straightforward once you understand the scoped-vs-singleton trap and make friends with cancellation tokens. They're one of those things where the framework gives you almost everything you need — you just have to actually use it.

If you've got work that doesn't fit neatly into a request/response cycle, `BackgroundService` is probably what you want. It's production-ready, testable, and plays nicely with everything else in the .NET hosting model.
