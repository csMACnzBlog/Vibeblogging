---
title: Feature Flags in .NET
date: 2026-04-11
tags: dotnet, aspnetcore, csharp, tutorial
image: feature-flags-in-dotnet.png
---

Shipping code is one thing. Deciding who sees it, and when, is another. Feature flags let you decouple deployment from release — you push code to production, but the feature stays off until you're ready to flip the switch. No special branch management, no coordinating deploys with marketing. Just a config change.

.NET has first-class support for this through `Microsoft.FeatureManagement`. It integrates with the standard `IConfiguration` system, works with filters for targeted rollouts, and has ASP.NET Core middleware for easy gating in web apps.

## Getting Started

Add the package:

```bash
dotnet add package Microsoft.FeatureManagement.AspNetCore
```

Or if you don't need the ASP.NET Core bits (console apps, background services):

```bash
dotnet add package Microsoft.FeatureManagement
```

Register it in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatureManagement();
```

By default, feature management reads from the `FeatureManagement` section in your configuration. Add some flags to `appsettings.json`:

```json
{
  "FeatureManagement": {
    "NewCheckout": true,
    "BetaDashboard": false
  }
}
```

## Using IFeatureManager

Inject `IFeatureManager` wherever you need it:

```csharp
public class CheckoutService
{
    private readonly IFeatureManager _featureManager;

    public CheckoutService(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    public async Task<CheckoutResult> ProcessAsync(Cart cart)
    {
        if (await _featureManager.IsEnabledAsync("NewCheckout"))
        {
            return await ProcessNewCheckoutAsync(cart);
        }

        return await ProcessLegacyCheckoutAsync(cart);
    }
}
```

`IsEnabledAsync` returns `true` if the flag is enabled, `false` otherwise. If the flag doesn't exist in configuration, it defaults to `false` — so you can safely add new flags in code before they exist in config.

## Strongly-Typed Feature Names

String literals scattered through the codebase are a maintenance headache. Define an enum or static class for your feature names:

```csharp
public static class FeatureFlags
{
    public const string NewCheckout = "NewCheckout";
    public const string BetaDashboard = "BetaDashboard";
    public const string DarkMode = "DarkMode";
}
```

Then use it consistently:

```csharp
if (await _featureManager.IsEnabledAsync(FeatureFlags.NewCheckout))
{
    // ...
}
```

Typos become compile-time errors instead of silent failures.

## Feature Filters

Simple on/off flags are useful, but real-world feature rollouts are more nuanced. You might want to enable a feature for 10% of users, or only in staging, or only for specific accounts. That's what feature filters are for.

`Microsoft.FeatureManagement` ships with built-in filters. Enable them when registering:

```csharp
builder.Services.AddFeatureManagement()
    .AddFeatureFilter<PercentageFilter>()
    .AddFeatureFilter<TimeWindowFilter>();
```

Configure them in `appsettings.json`:

```json
{
  "FeatureManagement": {
    "GradualRollout": {
      "EnabledFor": [
        {
          "Name": "Percentage",
          "Parameters": {
            "Value": 20
          }
        }
      ]
    },
    "HolidaySale": {
      "EnabledFor": [
        {
          "Name": "TimeWindow",
          "Parameters": {
            "Start": "2026-12-24T00:00:00",
            "End": "2026-12-26T00:00:00"
          }
        }
      ]
    }
  }
}
```

`GradualRollout` will return `true` for roughly 20% of calls. `HolidaySale` is only active between Christmas Eve and Boxing Day. No code changes needed to flip or schedule them — just update config.

## Targeting: Per-User Rollouts

The most powerful filter is `TargetingFilter`. It lets you enable a feature for specific users, groups, or a percentage of everyone else.

Register the targeting context accessor — `.WithTargeting<T>()` handles the rest:

```csharp
builder.Services.AddFeatureManagement()
    .WithTargeting<MyTargetingContextAccessor>();
```

The accessor tells the filter who the current user is:

```csharp
public class MyTargetingContextAccessor : ITargetingContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyTargetingContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ValueTask<TargetingContext> GetContextAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;

        return ValueTask.FromResult(new TargetingContext
        {
            UserId = user?.Identity?.Name ?? "anonymous",
            Groups = user?.Claims
                .Where(c => c.Type == "group")
                .Select(c => c.Value)
                .ToList() ?? []
        });
    }
}
```

Configure targeting in `appsettings.json`:

```json
{
  "FeatureManagement": {
    "BetaDashboard": {
      "EnabledFor": [
        {
          "Name": "Targeting",
          "Parameters": {
            "Audience": {
              "Users": ["alice@example.com", "bob@example.com"],
              "Groups": [
                {
                  "Name": "beta-testers",
                  "RolloutPercentage": 100
                }
              ],
              "DefaultRolloutPercentage": 5
            }
          }
        }
      ]
    }
  }
}
```

This enables `BetaDashboard` for Alice and Bob by name, for everyone in the `beta-testers` group, and for 5% of everyone else. The percentage is consistent for a given user ID — the same user always sees the same result, so they don't get a flickering experience.

## Gating Endpoints in ASP.NET Core

The `Microsoft.FeatureManagement.AspNetCore` package adds attribute and middleware support. Gate a controller action or Minimal API endpoint directly:

```csharp
// Controller
[HttpGet("new-dashboard")]
[FeatureGate(FeatureFlags.BetaDashboard)]
public IActionResult NewDashboard()
{
    return View();
}
```

```csharp
// Minimal API
app.MapGet("/new-dashboard", () => Results.Ok("New dashboard!"))
    .WithMetadata(new FeatureGateAttribute(FeatureFlags.BetaDashboard));
```

When the flag is off, the endpoint returns a 404 by default. You can customise that by replacing the disabled feature handler:

```csharp
builder.Services.AddFeatureManagement()
    .UseDisabledFeaturesHandler(new RedirectDisabledFeatureHandler());
```

```csharp
public class RedirectDisabledFeatureHandler : IDisabledFeaturesHandler
{
    public Task HandleDisabledFeatures(
        IEnumerable<string> features,
        ActionExecutingContext context)
    {
        context.Result = new RedirectResult("/coming-soon");
        return Task.CompletedTask;
    }
}
```

## Razor Views

You can use feature flags in Razor views too. Add the tag helper to `_ViewImports.cshtml`:

```csharp
@addTagHelper *, Microsoft.FeatureManagement.AspNetCore
```

Then gate sections of your markup:

```html
<feature name="BetaDashboard">
    <a href="/new-dashboard">Try the new dashboard →</a>
</feature>

<feature name="BetaDashboard" negate="true">
    <a href="/dashboard">Dashboard</a>
</feature>
```

The `negate` attribute flips the condition. Only one of those two links renders, depending on whether `BetaDashboard` is enabled.

## Testing with Feature Flags

Feature flags complicate unit testing slightly — you need to control which flags are on. The easiest approach is to create an `IFeatureManager` implementation that returns whatever you tell it:

```csharp
public class FakeFeatureManager : IFeatureManager
{
    private readonly Dictionary<string, bool> _flags;

    public FakeFeatureManager(params (string Flag, bool Enabled)[] flags)
    {
        _flags = flags.ToDictionary(f => f.Flag, f => f.Enabled);
    }

    public Task<bool> IsEnabledAsync(string feature)
        => Task.FromResult(_flags.TryGetValue(feature, out var enabled) && enabled);

    public Task<bool> IsEnabledAsync<TContext>(string feature, TContext context)
        => IsEnabledAsync(feature);

    public IAsyncEnumerable<string> GetFeatureNamesAsync()
        => _flags.Keys.ToAsyncEnumerable();
}
```

Use it in tests:

```csharp
[Fact]
public async Task Uses_new_checkout_when_flag_enabled()
{
    var featureManager = new FakeFeatureManager(
        (FeatureFlags.NewCheckout, true));

    var service = new CheckoutService(featureManager);
    var result = await service.ProcessAsync(new Cart());

    Assert.Equal(CheckoutType.New, result.CheckoutType);
}
```

For integration tests, you can override configuration in `WebApplicationFactory`:

```csharp
var factory = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:BetaDashboard"] = "true"
            });
        });
    });
```

The in-memory configuration overrides whatever is in `appsettings.json`, so you can exercise both the enabled and disabled paths.

## Worth Knowing

A few things that come up when working with feature flags in practice:

**Keep flag names in one place.** A `FeatureFlags` constants class is the minimum. As you add more flags, consider a registry or documentation page listing every active flag, what it controls, and who owns it. Flags that exist forever quietly become dead code.

**Clean up old flags.** Once a feature is fully rolled out and the old code path is deleted, remove the flag from config and the constants class. Stale flags are confusing and can cause bugs if someone re-uses a name for something different.

**Combine multiple flags with care.** Checking `IsEnabledAsync(A) && IsEnabledAsync(B)` leads to combinatorial explosion when testing. If two flags always go together, consider making them one flag.

**Azure App Configuration** integrates with `IFeatureManager` if you want centralised flag management with a UI, dynamic refresh, and targeting built in. The local JSON approach gets you started; App Configuration (or similar services like LaunchDarkly) takes it further without changing your application code.

## Decouple Deploy from Release

Feature flags are one of those tools that seem like overhead until the first time they save you from an emergency rollback. Deploy your code, verify it in production, gradually roll it out, then flip the flag globally when you're confident. It's a small configuration change — not a hotfix, not a revert, not a 2am deploy.

Start with `AddFeatureManagement()`, a couple of flags in `appsettings.json`, and a `FeatureFlags` constants class. That's enough for most teams most of the time.
