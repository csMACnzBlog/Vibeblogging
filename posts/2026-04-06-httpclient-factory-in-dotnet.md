---
title: HttpClient Factory in .NET
date: 2026-04-06
tags: aspnetcore, dotnet, csharp, tutorial
image: httpclient-factory-in-dotnet.png
---

If you've ever written `new HttpClient()` inside a method call, congratulations — you've probably introduced a socket exhaustion bug. It's one of the most common mistakes in .NET, and it's subtle enough that it only tends to manifest under load, long after you've shipped.

`IHttpClientFactory` is the fix. It manages the lifetime of `HttpClient` instances so you don't have to, and it plugs neatly into the DI system you're already using. Once you understand it, you'll never go back to the old way.

## The Problem with `new HttpClient()`

`HttpClient` implements `IDisposable`, so the obvious pattern is to create one per request and dispose it when you're done. That seems responsible. It isn't.

```csharp
// Don't do this
public async Task<string> GetDataAsync()
{
    using var client = new HttpClient();
    return await client.GetStringAsync("https://api.example.com/data");
}
```

When you dispose an `HttpClient`, the underlying socket isn't immediately freed — it lingers in `TIME_WAIT` state for a while. Under load, you'll chew through all your available sockets and start getting `SocketException` or `HttpRequestException` errors. Kubernetes restart loops, anyone?

The _other_ tempting fix — creating a single static `HttpClient` and sharing it — solves the socket exhaustion problem but introduces a different one: the client won't pick up DNS changes, because it holds connections open indefinitely. That's how you end up making requests to a server that's been decommissioned for six hours.

`IHttpClientFactory` threads the needle. It pools and reuses the underlying `HttpMessageHandler` (which owns the socket), but cycles those handlers on a schedule so DNS changes get picked up.

## Basic Setup

Add the factory to DI in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

var app = builder.Build();
```

Inject `IHttpClientFactory` wherever you need it, and create a client from it:

```csharp
public class WeatherService
{
    private readonly IHttpClientFactory _clientFactory;

    public WeatherService(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<string> GetForecastAsync()
    {
        var client = _clientFactory.CreateClient();
        var response = await client.GetAsync("https://api.weather.example.com/forecast");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
```

The factory manages the handler pool internally. You can call `CreateClient()` as often as you like without worrying about socket exhaustion. The clients themselves are cheap to create — they're just thin wrappers around the shared handler.

## Named Clients

If you're calling multiple external APIs, named clients let you configure each one independently. Register them with a name and set whatever defaults you need — base address, headers, timeouts:

```csharp
builder.Services.AddHttpClient("weather", client =>
{
    client.BaseAddress = new Uri("https://api.weather.example.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient("payments", client =>
{
    client.BaseAddress = new Uri("https://payments.example.com/api/");
    client.DefaultRequestHeaders.Add("X-Api-Version", "2");
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

Then request a named client when you need it:

```csharp
public async Task<string> GetForecastAsync()
{
    var client = _clientFactory.CreateClient("weather");
    return await client.GetStringAsync("forecast/today");
}
```

The `BaseAddress` is already set, so you just supply the relative path. This is a clean way to centralise per-API configuration and avoid scattering base URLs throughout your codebase.

## Typed Clients

Named clients are fine, but strings as identifiers have no type safety and the configuration is disconnected from where the client is used. Typed clients are cleaner.

A typed client is just a class that takes `HttpClient` in its constructor. The factory creates and configures the `HttpClient` for you, then hands it to the class:

```csharp
public class WeatherApiClient
{
    private readonly HttpClient _client;

    public WeatherApiClient(HttpClient client)
    {
        _client = client;
    }

    public async Task<WeatherForecast> GetForecastAsync()
    {
        var response = await _client.GetAsync("forecast/today");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WeatherForecast>()
            ?? throw new InvalidOperationException("Empty response from weather API.");
    }
}
```

Register it with `AddHttpClient<T>()`:

```csharp
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.weather.example.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

Now inject `WeatherApiClient` directly — no more `IHttpClientFactory` leaking into service classes:

```csharp
public class ForecastController : ControllerBase
{
    private readonly WeatherApiClient _weatherClient;

    public ForecastController(WeatherApiClient weatherClient)
    {
        _weatherClient = weatherClient;
    }

    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast()
    {
        var forecast = await _weatherClient.GetForecastAsync();
        return Ok(forecast);
    }
}
```

This is the pattern I reach for by default. Each external API gets its own typed client class that encapsulates all the request/response logic. The rest of the application never deals with raw `HttpClient` at all.

## Adding Resilience with Polly

Here's where it gets really useful. The `AddHttpClient` chain integrates directly with Polly's resilience pipelines via the `Microsoft.Extensions.Http.Resilience` package. You don't have to write retry logic by hand.

```bash
dotnet add package Microsoft.Extensions.Http.Resilience
```

The standard resilience handler covers retry, circuit breaker, and timeout in one call:

```csharp
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.weather.example.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler();
```

`AddStandardResilienceHandler` applies a sensible default pipeline: exponential backoff retry (up to three times), a circuit breaker that opens after a sustained failure rate, and a per-request timeout. For many services that's all you need.

If you want more control, compose the pipeline yourself:

```csharp
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.weather.example.com/");
})
.AddResilienceHandler("weather-pipeline", pipeline =>
{
    // Retry transient failures up to 3 times with exponential backoff
    pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(200)
    });

    // Open the circuit after 50% failure rate over 30 seconds
    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15)
    });

    // Per-attempt timeout
    pipeline.AddTimeout(TimeSpan.FromSeconds(5));
});
```

The `UseJitter` flag adds randomness to the retry delay so clients don't all retry in lockstep after a failure — that's the difference between a graceful recovery and an accidental DDoS on your own backend.

## Handling Authentication Headers

One common requirement is attaching an auth token to every outgoing request without repeating yourself in every method. The right tool for this is a delegating handler — a middleware layer that sits in the `HttpMessageHandler` pipeline:

```csharp
public class AuthTokenHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;

    public AuthTokenHandler(ITokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
```

Register the handler and wire it up to your typed client:

```csharp
builder.Services.AddTransient<AuthTokenHandler>();

builder.Services.AddHttpClient<PaymentsApiClient>(client =>
{
    client.BaseAddress = new Uri("https://payments.example.com/api/");
})
.AddHttpMessageHandler<AuthTokenHandler>();
```

Every request from `PaymentsApiClient` now gets the auth header automatically — no changes to the client class itself. You can chain multiple handlers this way, and they compose cleanly with the resilience pipeline.

## Putting It Together

Here's the pattern I use for a production service that calls an external API:

```csharp
// Typed client encapsulates all the HTTP details
public class OrdersApiClient
{
    private readonly HttpClient _client;
    private readonly ILogger<OrdersApiClient> _logger;

    public OrdersApiClient(HttpClient client, ILogger<OrdersApiClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Order?> GetOrderAsync(Guid id)
    {
        var response = await _client.GetAsync($"orders/{id}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Order>();
    }

    public async Task<Guid> CreateOrderAsync(CreateOrderRequest request)
    {
        var response = await _client.PostAsJsonAsync("orders", request);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<Order>()
            ?? throw new InvalidOperationException("Empty response from orders API.");

        _logger.LogInformation("Created order {OrderId}", created.Id);
        return created.Id;
    }
}
```

```csharp
// Registration with auth handler, resilience, and timeouts
builder.Services.AddTransient<AuthTokenHandler>();

builder.Services.AddHttpClient<OrdersApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OrdersApi:BaseUrl"]
        ?? throw new InvalidOperationException("OrdersApi:BaseUrl not configured"));
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<AuthTokenHandler>()
.AddStandardResilienceHandler();
```

Base URL comes from configuration, auth is handled by a dedicated handler, resilience is wired in at registration time. The client class stays focused on what the API does — not on how to call it reliably.

## Worth the Boilerplate

The upfront cost of `IHttpClientFactory` is a few extra lines of registration code. The payoff is correct socket lifetime management, centralised per-API configuration, and easy composition with resilience policies.

There are very few cases where reaching for `new HttpClient()` directly is the right call. Once you've hit a socket exhaustion issue in production — or spent a day debugging why requests are still going to a server that should have been unreachable hours ago — you'll appreciate the guardrails.
