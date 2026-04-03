---
title: The Options Pattern in .NET
date: 2026-04-03
tags: dotnet, csharp, aspnetcore, configuration, tutorial
image: the-options-pattern-in-dotnet.png
---

Configuration is one of those things that looks simple until it isn't. You start by reading a connection string from `appsettings.json`, and before long you've got a sprawling mix of `IConfiguration.GetSection("Foo:Bar:Baz")` calls scattered across your codebase, magic string keys duplicated everywhere, and absolutely no idea which parts of the app break if you rename a setting.

The Options pattern is .NET's answer to this. It gives you strongly typed, validated, injectable configuration that works with the same DI system you already use for everything else.

## The Basics

Start with a plain class that mirrors your configuration section:

```csharp
public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
}
```

Then in `appsettings.json`:

```json
{
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587,
    "UseSsl": true,
    "Username": "no-reply@example.com",
    "Password": "hunter2"
  }
}
```

Bind them together in `Program.cs`:

```csharp
builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Smtp"));
```

That's it. .NET reads the `Smtp` section, maps it to `SmtpOptions` by property name, and registers the result in the DI container.

## Injecting Options

The options system exposes three interfaces. The one you'll use most is `IOptions<T>`:

```csharp
public class EmailService
{
    private readonly SmtpOptions _options;

    public EmailService(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        using var client = new SmtpClient(_options.Host, _options.Port);
        client.EnableSsl = _options.UseSsl;
        // ...
    }
}
```

`IOptions<T>.Value` returns the bound options object. It's a singleton — the same instance every time, resolved at first access. That's exactly what you want for settings that don't change while the app is running.

## Options That Can Change: IOptionsSnapshot and IOptionsMonitor

`IOptions<T>` is computed once. If someone edits `appsettings.json` while the app is running, `IOptions<T>` won't see the new values. For most configuration that's fine — a restart is expected after a config change. But sometimes you genuinely need live reloading.

That's where `IOptionsSnapshot<T>` comes in:

```csharp
public class FeatureFlagService
{
    private readonly IOptionsSnapshot<FeatureFlags> _options;

    public FeatureFlagService(IOptionsSnapshot<FeatureFlags> options)
    {
        _options = options;
    }

    public bool IsEnabled(string feature) =>
        _options.Value.EnabledFeatures.Contains(feature);
}
```

`IOptionsSnapshot<T>` is scoped — it's recalculated once per request (or per scope). If someone updates the config file between requests, the next request picks up the new values. Handy for feature flags, thresholds, and anything you want to tune without restarting.

`IOptionsMonitor<T>` goes one step further — it's a singleton that gets notified when values change and lets you react immediately:

```csharp
public class RateLimiterService
{
    private RateLimiterOptions _current;

    public RateLimiterService(IOptionsMonitor<RateLimiterOptions> monitor)
    {
        _current = monitor.CurrentValue;
        monitor.OnChange(updated => _current = updated);
    }

    public bool AllowRequest(string clientId)
    {
        // use _current.MaxRequestsPerMinute
        return true;
    }
}
```

Use `IOptionsMonitor<T>` when you need a singleton (like a background service) to react to config changes in real time. For everything else, `IOptionsSnapshot<T>` or `IOptions<T>` is usually simpler.

Here's a quick cheat sheet:

| Interface | Lifetime | Reloads? | Use when... |
|---|---|---|---|
| `IOptions<T>` | Singleton | No | Settings don't change at runtime |
| `IOptionsSnapshot<T>` | Scoped | Per scope | Transient config, feature flags |
| `IOptionsMonitor<T>` | Singleton | Yes, via callback | Singletons that need live updates |

## Validation

The bind-and-hope approach is a code smell. If someone types `"smtp.exampl.com"` instead of `"smtp.example.com"`, your app starts fine and fails at runtime when it tries to send an email.

You can add validation to catch this early. The simplest way is `DataAnnotations`:

```csharp
public class SmtpOptions
{
    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;
}
```

Then tell the options system to validate them:

```csharp
builder.Services.AddOptions<SmtpOptions>()
    .BindConfiguration("Smtp")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

`ValidateOnStart()` is the important bit — it forces validation to run when the host starts, before any requests arrive. Without it, validation only happens the first time the options are accessed. By then it might be too late.

For more complex rules, you can implement `IValidateOptions<T>`:

```csharp
public class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        if (options.UseSsl && options.Port == 25)
        {
            return ValidateOptionsResult.Fail(
                "Port 25 is not appropriate for SSL connections. Use 465 or 587.");
        }

        return ValidateOptionsResult.Success;
    }
}
```

Register it with the DI container:

```csharp
builder.Services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();
```

This is where you put cross-property validation that `DataAnnotations` can't express.

## Named Options

Sometimes you have multiple instances of the same configuration shape. Think multiple external services with different endpoints, or multiple database connections. Named options handle this elegantly:

```csharp
builder.Services.Configure<HttpClientOptions>("Payments", options =>
{
    options.BaseUrl = "https://payments.example.com";
    options.TimeoutSeconds = 30;
});

builder.Services.Configure<HttpClientOptions>("Notifications", options =>
{
    options.BaseUrl = "https://notifications.example.com";
    options.TimeoutSeconds = 10;
});
```

Resolve by name using `IOptionsMonitor<T>`:

```csharp
public class PaymentGateway
{
    private readonly HttpClientOptions _options;

    public PaymentGateway(IOptionsMonitor<HttpClientOptions> monitor)
    {
        _options = monitor.Get("Payments");
    }
}
```

`monitor.Get("Payments")` returns the named instance. If you ask for a name that doesn't exist, you get a default-constructed instance — so keep your names consistent.

## Putting It Together with AddOptions

The fluent `AddOptions<T>()` API is cleaner than calling `Configure<T>()` and `ValidateDataAnnotations()` separately. Here's what a production-ready setup looks like:

```csharp
builder.Services.AddOptions<SmtpOptions>()
    .BindConfiguration("Smtp")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();

builder.Services.AddScoped<EmailService>();
```

If you need multiple sections bound to one class, you can also bind explicitly:

```csharp
builder.Services.AddOptions<DatabaseOptions>()
    .Configure<IConfiguration>((opts, config) =>
    {
        opts.ConnectionString = config.GetConnectionString("Default")!;
        opts.CommandTimeout = config.GetValue<int>("Database:CommandTimeout");
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

The lambda gets the configuration injected, so you can pull from multiple places without being forced into one section.

## Testing With Options

One of the nicest things about the Options pattern is how testable it makes configuration-dependent code. Instead of mocking `IConfiguration` (which is annoying), you just create the options object directly:

```csharp
[Fact]
public async Task SendAsync_UsesConfiguredHost()
{
    var options = Options.Create(new SmtpOptions
    {
        Host = "localhost",
        Port = 25,
        UseSsl = false
    });

    var service = new EmailService(options);

    // test away
}
```

`Options.Create(value)` returns an `IOptions<T>` that wraps your test value. No fuss. No mocking framework required. Your service doesn't know or care that it's running in a test.

For `IOptionsSnapshot<T>`, the test helper is slightly different since it's scoped — but the principle is the same. Construct the object you want, wrap it, inject it.

The Options pattern is one of those things I wish I'd learned earlier. Once you start using it, reading raw `IConfiguration` strings directly feels like a step backwards. Strongly typed settings, validated at startup, injectable anywhere — it's just better.
