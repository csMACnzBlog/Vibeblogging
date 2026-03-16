---
title: Async/Await Pitfalls in C#
date: 2026-03-16
tags: dotnet, csharp, async, performance
image: async-await-pitfalls-in-csharp.png
---

Async/await is one of the best things that happened to C#. It turned callback spaghetti into readable, linear code. But it comes with a set of traps that are easy to fall into and sometimes hard to diagnose — deadlocks that only appear in production, exceptions that silently disappear, tasks that seem to run fine until they don't.

This post walks through the most common async/await mistakes I see in .NET codebases, with enough context to understand *why* they happen and how to fix them.

## async void Is Almost Always Wrong

The rule is simple: if you're writing an async method that returns nothing, use `Task`, not `void`.

```csharp
// Don't do this
async void LoadDataAsync()
{
    var data = await FetchDataAsync();
    ProcessData(data);
}

// Do this instead
async Task LoadDataAsync()
{
    var data = await FetchDataAsync();
    ProcessData(data);
}
```

Here's the problem with `async void`: when an exception is thrown inside it, there's no `Task` for the caller to observe. The exception gets rethrown on the synchronisation context — which in a console app means the thread pool, and in a Windows app means the UI dispatcher. Either way, you're looking at an application crash that produces a cryptic, hard-to-debug error.

With `async Task`, the exception is captured inside the returned `Task` and surfaces when the caller awaits it. You can catch it, log it, or handle it gracefully.

The one legitimate use of `async void` is event handlers, because event handler delegates have a `void` return type and you can't change that:

```csharp
// Event handlers are the exception
private async void Button_Click(object sender, EventArgs e)
{
    await DoSomethingAsync();
}
```

Even here, wrap the body in a try/catch so you handle exceptions yourself rather than letting them propagate to the sync context unhandled.

## Deadlocks with .Result and .Wait()

This one has bitten almost every .NET developer at least once. You have an async method, you're in a synchronous context (maybe an old ASP.NET MVC action, maybe a constructor), and you reach for `.Result` or `.Wait()`:

```csharp
// This can deadlock in ASP.NET and UI apps
public string GetData()
{
    return FetchDataAsync().Result;  // blocks the thread
}
```

Here's what happens. `FetchDataAsync()` suspends at an `await` inside it. When the awaited operation completes, the continuation needs to resume on the captured synchronisation context — the ASP.NET request context, or the UI thread. But that thread is blocked, waiting for the task to complete. Classic deadlock.

The fix is to go async all the way:

```csharp
public async Task<string> GetDataAsync()
{
    return await FetchDataAsync();
}
```

If you absolutely can't make the calling method async — and this should be rare — use `Task.Run` to move the work off the synchronisation context entirely:

```csharp
// Last resort — not great, but avoids the deadlock
public string GetData()
{
    return Task.Run(() => FetchDataAsync()).GetAwaiter().GetResult();
}
```

Console apps don't have a synchronisation context by default, so `.Result` is less dangerous there — but it still blocks a thread, which is wasteful. The rule of thumb: async all the way down, or don't mix sync and async.

## Not Awaiting Tasks

A task that isn't awaited is a fire-and-forget operation. Sometimes that's intentional. Often it isn't.

```csharp
public async Task SaveRecordAsync(Record record)
{
    ValidateRecord(record);
    WriteAuditLogAsync(record);  // missing await — this runs, but we don't know if it succeeds
    await _repository.SaveAsync(record);
}
```

`WriteAuditLogAsync` will run, but any exception it throws will be silently swallowed. You won't know if it failed. In older versions of .NET, an unobserved task exception would eventually crash the process via `TaskScheduler.UnobservedTaskException`. In .NET 4.5+ the default behaviour changed to swallowing these silently — which is arguably worse, because failures become invisible.

If you want fire-and-forget, be explicit about it:

```csharp
// Explicit fire-and-forget with exception handling
_ = Task.Run(async () =>
{
    try
    {
        await WriteAuditLogAsync(record);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Audit log write failed");
    }
});
```

Using `_ =` makes it clear you're intentionally discarding the task. The try/catch ensures failures are at least logged. For most production code, though, you should be awaiting your tasks rather than firing and forgetting — it makes behaviour predictable and errors visible.

## ConfigureAwait(false) and When It Matters

When you `await` a task, the continuation (the code after the `await`) is scheduled to resume on the captured synchronisation context by default. In a UI app, that means the UI thread. In ASP.NET, that means the request context.

`ConfigureAwait(false)` tells the awaiter not to capture the context — the continuation can run on any available thread pool thread.

```csharp
// Library code — use ConfigureAwait(false)
public async Task<string> ReadFileAsync(string path)
{
    using var reader = new StreamReader(path);
    return await reader.ReadToEndAsync().ConfigureAwait(false);
}

// Application code — usually fine without it
public async Task<IActionResult> GetUserAsync(int id)
{
    var user = await _userService.GetByIdAsync(id);  // no ConfigureAwait needed here
    return Ok(user);
}
```

The rule is: use `ConfigureAwait(false)` in library code; skip it in application code.

Library code shouldn't assume anything about the caller's synchronisation context. Using `ConfigureAwait(false)` prevents the library from accidentally blocking a UI thread or an ASP.NET request context. It also avoids the deadlock scenario described earlier when callers block on tasks.

In application code — your controllers, your Blazor components, your WPF view models — you typically *want* to resume on the synchronisation context, because the code after the `await` often touches UI elements or request-scoped services that require it.

Starting with ASP.NET Core, there's no synchronisation context at all, so `ConfigureAwait(false)` has no effect in most ASP.NET Core code. But it's still good practice in libraries for portability.

## Async in Constructors

This catches people out. C# constructors are synchronous — you can't `await` inside one:

```csharp
// This won't compile
public class DataService
{
    public DataService()
    {
        _data = await LoadDataAsync();  // error: can't await in a constructor
    }
}
```

The two patterns that work are factory methods and lazy initialisation.

Factory methods are the cleaner approach when you need the data before the object is usable:

```csharp
public class DataService
{
    private readonly ImmutableList<Record> _data;

    private DataService(ImmutableList<Record> data)
    {
        _data = data;
    }

    public static async Task<DataService> CreateAsync()
    {
        var data = await LoadDataAsync();
        return new DataService(data);
    }
}

// Usage
var service = await DataService.CreateAsync();
```

Lazy initialisation works when you want to defer loading until first use:

```csharp
public class DataService
{
    private readonly Lazy<Task<ImmutableList<Record>>> _data =
        new Lazy<Task<ImmutableList<Record>>>(LoadDataAsync);

    public async Task<ImmutableList<Record>> GetDataAsync()
        => await _data.Value;
}
```

Both approaches are explicit about the async work involved, which makes the code easier to reason about.

## Threading CancellationToken Through

Not passing `CancellationToken` through your async call chain is one of those mistakes that seems fine until you care about responsiveness or resource efficiency.

Consider a web API handler that does several async operations:

```csharp
// Missing cancellation support
public async Task<Report> GenerateReportAsync(int userId)
{
    var user = await _userRepo.GetByIdAsync(userId);
    var orders = await _orderRepo.GetOrdersAsync(userId);
    var report = await _reportBuilder.BuildAsync(user, orders);
    return report;
}
```

If the HTTP request is cancelled — the client disconnected, or a load balancer timed out — this method keeps running. It queries the database, builds the report, allocates memory, all for work that nobody needs. Under load, this can exhaust your connection pool and slow down every request.

The fix is to accept and pass through a `CancellationToken`:

```csharp
public async Task<Report> GenerateReportAsync(int userId, CancellationToken cancellationToken = default)
{
    var user = await _userRepo.GetByIdAsync(userId, cancellationToken);
    var orders = await _orderRepo.GetOrdersAsync(userId, cancellationToken);
    var report = await _reportBuilder.BuildAsync(user, orders, cancellationToken);
    return report;
}
```

In ASP.NET Core, you can inject `CancellationToken` directly into controller actions or minimal API handlers — the framework binds it to the request's cancellation token automatically:

```csharp
app.MapGet("/report/{userId}", async (int userId, CancellationToken ct) =>
{
    var report = await reportService.GenerateReportAsync(userId, ct);
    return Results.Ok(report);
});
```

The `= default` on the parameter makes the token optional, which is useful in unit tests where you don't want to construct one manually.

## ValueTask vs Task: Pick the Right One

`Task` allocates an object on the heap every time. For high-throughput code — APIs handling thousands of requests per second, tight loops — that allocation adds up.

`ValueTask` is a struct that avoids the allocation when the result is available synchronously (or near-synchronously). The common case is a cache:

```csharp
public async ValueTask<User> GetUserAsync(int id, CancellationToken ct = default)
{
    if (_cache.TryGetValue(id, out var cached))
        return cached;  // returns synchronously — no Task allocation

    var user = await _database.QueryAsync<User>(id, ct);
    _cache.Set(id, user);
    return user;
}
```

When the user is in the cache, this returns without ever going async. `ValueTask` makes that zero-allocation. When the user isn't cached, it falls back to a real async operation.

The catch: **never await a `ValueTask` more than once**. A `Task` can be awaited multiple times safely. A `ValueTask` can't — it may have already been recycled by the time you await it again.

```csharp
// This is fine with Task, dangerous with ValueTask
var task = GetUserAsync(42);
var user1 = await task;  // ok
var user2 = await task;  // fine for Task, broken for ValueTask
```

If you need to await multiple times, call `.AsTask()` to convert it first:

```csharp
var task = GetUserAsync(42).AsTask();
var user1 = await task;
var user2 = await task;  // safe now
```

Use `ValueTask` when: you're writing high-throughput library code, the synchronous completion path is common, and you're not going to await the result multiple times. For most application code, `Task` is fine and its simpler semantics are worth more than the allocation savings.

## Putting It Together

The async/await pitfalls in C# follow a pattern: they all involve either ignoring exceptions, blocking threads, or making assumptions about context that don't hold.

The short version:
- Never `async void` except event handlers (and even then, add a try/catch)
- Never block with `.Result` or `.Wait()` unless you really understand the context
- Always await your tasks, or explicitly handle fire-and-forget with proper error logging
- Use `ConfigureAwait(false)` in library code
- Use factory methods or lazy init instead of async constructors
- Pass `CancellationToken` all the way through your call chain
- Reach for `ValueTask` in hot paths; don't await it more than once

Most of these become second nature once you've seen them in a real debugging session. The deadlock one in particular — once you've tracked that down in a production outage, you never block on async code again.
