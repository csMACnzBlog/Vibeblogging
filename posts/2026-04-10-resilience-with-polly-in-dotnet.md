---
title: Resilience with Polly in .NET
date: 2026-04-10
tags: dotnet, aspnetcore, csharp, tutorial
image: resilience-with-polly-in-dotnet.png
---

Distributed systems fail. A downstream API goes slow, a database hiccups, a network packet gets lost. Your code can either pretend that doesn't happen (and get paged at 2am) or handle it gracefully. Polly is the standard .NET library for exactly that: retry policies, circuit breakers, timeouts, and more.

If you covered [HttpClientFactory](httpclient-factory-in-dotnet.html) earlier in this series, Polly integrates directly with it. But you can use Polly on any async operation — not just HTTP.

## What Is Polly?

Polly lets you define resilience policies and wrap your code with them. A policy is a rule like "retry up to 3 times with exponential backoff" or "stop calling this service for 30 seconds after 5 consecutive failures".

Add the packages:

```bash
dotnet add package Polly.Core
dotnet add package Microsoft.Extensions.Http.Resilience
```

`Polly.Core` gives you the core pipeline API. `Microsoft.Extensions.Http.Resilience` wraps it into the `IHttpClientBuilder` extension methods for easy integration with `HttpClientFactory`.

## Retry Policies

The most common policy. When an operation fails with a transient error, wait a bit and try again.

```csharp
using Polly;
using Polly.Retry;

var retryPipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutException>()
    })
    .Build();

await retryPipeline.ExecuteAsync(async cancellationToken =>
{
    var response = await httpClient.GetAsync("/api/products", cancellationToken);
    response.EnsureSuccessStatusCode();
});
```

A few things worth noting here:

- `BackoffType = Exponential` means the delay doubles each attempt: 1s, 2s, 4s
- `UseJitter = true` adds a random offset to each delay — without it, retrying clients tend to hammer the server in sync
- `ShouldHandle` controls which exceptions trigger a retry; be selective here, don't retry validation errors or 404s

## Circuit Breaker

The circuit breaker pattern prevents your code from repeatedly calling a service that's clearly down. After a threshold of failures, the circuit "opens" and subsequent calls fail fast without even attempting the operation. After a timeout, a single call is allowed through to test whether things have recovered.

```csharp
using Polly.CircuitBreaker;

var circuitBreakerPipeline = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 10,
        BreakDuration = TimeSpan.FromSeconds(15),
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
    })
    .Build();
```

Reading those options:

- 50% failure ratio (`FailureRatio = 0.5`) triggers the break
- Measured over a 30-second window (`SamplingDuration`)
- Only triggers after at least 10 calls (`MinimumThroughput`), so a cold start with 1 failure doesn't open the circuit
- The circuit stays open for 15 seconds (`BreakDuration`) before allowing a test call

When the circuit is open, `BrokenCircuitException` is thrown immediately — the operation isn't attempted. Callers should handle this distinctly from transient failures.

## Timeout

A timeout policy cancels the operation if it doesn't complete in time. This is different from the `CancellationToken` you might already be passing — Polly's timeout creates a fresh token that fires independently:

```csharp
using Polly.Timeout;

var timeoutPipeline = new ResiliencePipelineBuilder()
    .AddTimeout(new TimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(5),
        OnTimeout = args =>
        {
            Console.WriteLine($"Operation timed out after {args.Timeout.TotalSeconds}s");
            return ValueTask.CompletedTask;
        }
    })
    .Build();
```

When the timeout fires, the inner `CancellationToken` is cancelled and a `TimeoutRejectedException` is thrown.

## Combining Policies

Individual policies work fine in isolation, but real resilience comes from combining them. Polly's pipeline executes strategies in order, outer to inner:

```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddTimeout(new TimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(10)   // overall timeout
    })
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()  // retry on per-attempt timeout too
    })
    .AddTimeout(new TimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(2)    // per-attempt timeout
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 10,
        BreakDuration = TimeSpan.FromSeconds(15),
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
    })
    .Build();
```

This pipeline:

1. Applies a 10-second overall timeout
2. Retries up to 3 times (including if a per-attempt timeout fires)
3. Applies a 2-second per-attempt timeout
4. Checks the circuit breaker before each attempt

Order matters. Putting the circuit breaker innermost means it sees the actual failures and can make good decisions about when to break.

## Integration with HttpClientFactory

The cleanest way to use Polly in an ASP.NET Core app is through `IHttpClientBuilder`. The `Microsoft.Extensions.Http.Resilience` package provides extension methods that build the pipeline for you:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("ProductsApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
})
.AddStandardResilienceHandler();
```

`AddStandardResilienceHandler()` configures a sensible default pipeline: rate limiting, total request timeout, retry with exponential backoff, per-attempt timeout, and circuit breaker — all with conservative defaults suitable for most external HTTP calls. You can inspect and override the defaults:

```csharp
builder.Services.AddHttpClient("ProductsApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
})
.AddResilienceHandler("products-pipeline", pipelineBuilder =>
{
    pipelineBuilder
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 20,
            BreakDuration = TimeSpan.FromSeconds(30),
        })
        .AddTimeout(TimeSpan.FromSeconds(5));
});
```

`HttpRetryStrategyOptions` and `HttpCircuitBreakerStrategyOptions` are pre-configured to handle `HttpRequestException` and non-successful HTTP status codes (5xx, 408) by default. You don't need to configure `ShouldHandle` unless you want to customise it.

## Typed Clients and Resilience

If you're using typed `HttpClient` clients, the pipeline registration sits on the builder:

```csharp
public class ProductsClient
{
    private readonly HttpClient _httpClient;

    public ProductsClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/products", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Product>>(cancellationToken)
            ?? [];
    }
}
```

```csharp
builder.Services
    .AddHttpClient<ProductsClient>(client =>
    {
        client.BaseAddress = new Uri("https://api.example.com");
    })
    .AddStandardResilienceHandler();
```

The `ProductsClient` is unaware of the resilience pipeline — it just calls `_httpClient` as normal. Polly intercepts the `SendAsync` method on the underlying handler.

## Testing with Polly

When writing tests, you usually don't want policies to actually sleep or wait. Polly respects `CancellationToken`, but for controlled testing you can stub the policy itself, or use the `ResiliencePipelineBuilder` with overridden delay:

```csharp
public class RetryPipelineTests
{
    [Fact]
    public async Task Retry_SucceedsOnThirdAttempt()
    {
        int callCount = 0;

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,   // no real delay in tests
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder()
                    .Handle<InvalidOperationException>()
            })
            .Build();

        await pipeline.ExecuteAsync(async _ =>
        {
            callCount++;
            if (callCount < 3)
                throw new InvalidOperationException("Not yet");

            await Task.CompletedTask;
        });

        Assert.Equal(3, callCount);
    }
}
```

Setting `Delay = TimeSpan.Zero` keeps tests fast. The logic is still fully exercised — you're just skipping the wall clock wait.

For integration tests involving typed clients, use `WireMock.Net` or similar to simulate slow or failing endpoints, then verify your client behaves correctly (returns fallback data, throws after max retries, etc.).

## Worth Knowing

A few things that come up in practice:

**Don't retry non-transient errors.** 400 Bad Request means the request is wrong — retrying it won't help. Scope `ShouldHandle` to network errors, timeouts, and 5xx responses. If you're using `HttpRetryStrategyOptions`, it handles this by default.

**Log on retry.** Polly has `OnRetry` and `OnCircuitOpened` callbacks. Use them to emit log entries:

```csharp
.AddRetry(new RetryStrategyOptions
{
    OnRetry = args =>
    {
        logger.LogWarning(
            args.Outcome.Exception,
            "Retry {AttemptNumber} after {Delay}ms",
            args.AttemptNumber,
            args.RetryDelay.TotalMilliseconds);
        return ValueTask.CompletedTask;
    }
})
```

Those log entries are invaluable when diagnosing whether a downstream service is flaky.

**Circuit breaker state is shared.** When registered via DI, the same `ResiliencePipeline` instance is used across all requests. That's what you want for circuit breakers — they need to track state across multiple callers. If you instantiate a pipeline per-request, circuit breakers don't work as expected.

**Polly v8 vs v7.** The fluent API changed significantly in Polly v8. If you're reading older blog posts or Stack Overflow answers, the syntax looks different. The concepts are the same; the API is now `ResiliencePipelineBuilder` instead of `Policy.Handle<>()`.

## Resilience Is a Feature

Adding a retry policy isn't defensive programming — it's just correct. Networks are unreliable, services have bad moments, dependencies go down for maintenance. Handling those cases explicitly, with backoff and circuit breaking, makes the difference between an app that degrades gracefully and one that falls over the moment anything goes wrong.

Start with `AddStandardResilienceHandler()` on your HTTP clients. Tune the defaults as you learn how your dependencies actually behave in production. That's usually all you need.
