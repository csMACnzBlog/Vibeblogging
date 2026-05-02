---
title: TimeProvider in .NET
date: 2026-05-02
tags: dotnet, csharp, testing, tutorial
image: timeprovider-in-dotnet.png
---

If you've ever written code that calls `DateTime.UtcNow` or `DateTimeOffset.Now` directly, you've probably felt the pain in tests. The current time is hard to control, which means any test that depends on it either becomes flaky or requires awkward workarounds. `TimeProvider` — introduced in .NET 8 — is the official answer to this problem.

## The Problem with Static Time

Consider a token expiry check:

```csharp
public bool IsTokenExpired(DateTimeOffset issuedAt, TimeSpan validity)
{
    return DateTimeOffset.UtcNow > issuedAt + validity;
}
```

To test this reliably you'd have to either sleep the thread (slow and fragile) or manipulate the system clock (not an option). Most teams end up with a custom `IDateTimeProvider` interface that wraps `UtcNow`. That works, but everyone reinvents it slightly differently.

`TimeProvider` standardises the pattern. It's an abstract class in `System.Threading` that ships in .NET 8+ and is also available via the `Microsoft.Extensions.TimeProvider.Testing` NuGet package for older targets.

## The Basics

`TimeProvider` is abstract with one key method to care about:

```csharp
public abstract DateTimeOffset GetUtcNow();
```

It also exposes:

```csharp
public virtual DateTimeOffset GetLocalNow();
public virtual long GetTimestamp();
public virtual long TimestampFrequency { get; }
public virtual TimeZoneInfo LocalTimeZone { get; }
```

And it provides timer creation:

```csharp
public virtual ITimer CreateTimer(
    TimerCallback callback,
    object? state,
    TimeSpan dueTime,
    TimeSpan period);
```

The important thing is that `TimeProvider.System` gives you the real implementation backed by `DateTimeOffset.UtcNow`. You use that in production, and swap it in tests.

## Using TimeProvider in Services

The pattern is simple: take `TimeProvider` as a constructor parameter instead of calling `DateTime` statics directly.

```csharp
public class TokenService
{
    private readonly TimeProvider _timeProvider;

    public TokenService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool IsTokenExpired(DateTimeOffset issuedAt, TimeSpan validity)
    {
        return _timeProvider.GetUtcNow() > issuedAt + validity;
    }

    public string GenerateToken(int userId)
    {
        var now = _timeProvider.GetUtcNow();
        var expiry = now.AddHours(1);
        return $"{userId}:{now.ToUnixTimeSeconds()}:{expiry.ToUnixTimeSeconds()}";
    }
}
```

Nothing complex here. The service doesn't know whether it's running in production or tests — it just uses whatever `TimeProvider` it receives.

## Testing with FakeTimeProvider

The `Microsoft.Extensions.TimeProvider.Testing` package provides `FakeTimeProvider`, which lets you set and advance time programmatically:

```csharp
using Microsoft.Extensions.Time.Testing;

[Fact]
public void IsTokenExpired_ReturnsFalse_WhenWithinValidity()
{
    var fakeTime = new FakeTimeProvider();
    fakeTime.SetUtcNow(new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero));

    var service = new TokenService(fakeTime);
    var issuedAt = new DateTimeOffset(2026, 5, 2, 9, 30, 0, TimeSpan.Zero);

    var result = service.IsTokenExpired(issuedAt, TimeSpan.FromHours(1));

    Assert.False(result); // 10:00 is within the 1-hour window
}

[Fact]
public void IsTokenExpired_ReturnsTrue_AfterValidityPeriod()
{
    var fakeTime = new FakeTimeProvider();
    fakeTime.SetUtcNow(new DateTimeOffset(2026, 5, 2, 10, 31, 0, TimeSpan.Zero));

    var service = new TokenService(fakeTime);
    var issuedAt = new DateTimeOffset(2026, 5, 2, 9, 30, 0, TimeSpan.Zero);

    var result = service.IsTokenExpired(issuedAt, TimeSpan.FromHours(1));

    Assert.True(result); // 10:31 is past the 1-hour window
}
```

You can also advance time with `Advance`:

```csharp
[Fact]
public void Advance_MovesTimeForward()
{
    var fakeTime = new FakeTimeProvider(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    fakeTime.Advance(TimeSpan.FromDays(30));

    Assert.Equal(
        new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero),
        fakeTime.GetUtcNow());
}
```

`Advance` is particularly useful when testing code that checks time across multiple steps — you can move the clock forward between operations without hardcoding absolute timestamps everywhere.

## Registering in ASP.NET Core

In production code, register `TimeProvider.System` as the singleton:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

Your services that take `TimeProvider` in their constructors will receive the real implementation automatically. In integration tests you can replace it:

```csharp
var fakeTime = new FakeTimeProvider();
fakeTime.SetUtcNow(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));

var factory = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(builder =>
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(fakeTime);
        });
    });

var client = factory.CreateClient();
```

Now every service in the application uses `fakeTime`, and you can advance the clock between requests to test time-sensitive flows without sleeping.

## Timers

`TimeProvider` also abstracts timer creation, which lets you test timer-driven logic without real delays:

```csharp
public class CacheReaper
{
    private readonly ITimer _timer;

    public CacheReaper(TimeProvider timeProvider, CacheService cache)
    {
        _timer = timeProvider.CreateTimer(
            _ => cache.RemoveExpired(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }
}
```

In tests, `FakeTimeProvider.Advance` triggers timers whose due time has passed:

```csharp
[Fact]
public void CacheReaper_RemovesExpiredEntries_AfterOneTick()
{
    var fakeTime = new FakeTimeProvider();
    var cache = new CacheService();
    cache.Add("key", "value", expiresIn: TimeSpan.FromSeconds(30));

    var reaper = new CacheReaper(fakeTime, cache);

    // Advance past the timer period
    fakeTime.Advance(TimeSpan.FromMinutes(2));

    Assert.False(cache.Contains("key"));
}
```

This is something you couldn't test at all without either a real delay or a complicated manual timer mock.

## GetElapsed and Timestamps

For measuring durations rather than wall-clock time, `TimeProvider` provides timestamp-based APIs:

```csharp
public class OperationTimer
{
    private readonly TimeProvider _timeProvider;

    public OperationTimer(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public TimeSpan MeasureOperation(Action operation)
    {
        var start = _timeProvider.GetTimestamp();
        operation();
        var elapsed = _timeProvider.GetElapsedTime(start);
        return elapsed;
    }
}
```

`GetElapsedTime` is a convenience extension that converts from timestamps to `TimeSpan` using `TimestampFrequency`. In production this uses a high-resolution timer; `FakeTimeProvider` lets you control it too.

## Wrapping Up

`TimeProvider` is one of those small additions that makes a noticeable quality-of-life difference. It replaces the ad-hoc `IDateTimeProvider` interfaces that most codebases accumulate, gives you a standardised pattern everyone on the team can follow, and pairs with `FakeTimeProvider` to make time-dependent tests fast and deterministic.

The migration is low friction: swap your `DateTime.UtcNow` calls to `_timeProvider.GetUtcNow()`, register `TimeProvider.System` in your DI container, and inject `FakeTimeProvider` in tests. Once you've done it a few times it becomes second nature — and you'll wonder how you put up with the alternatives.
