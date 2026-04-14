---
title: Using Json.NET in ASP.NET Core on .NET 10
date: 2026-04-14
tags: dotnet, aspnetcore, csharp, json, tutorial
image: json-net-in-aspnet-core-on-dotnet-10.png
---

ASP.NET Core has shipped `System.Text.Json` as its default serializer since .NET Core 3.0. It's fast, it's trim-safe, and it keeps getting better. But Json.NET (Newtonsoft.Json) is still everywhere — in legacy codebases, in third-party libraries, and in projects that need features STJ still doesn't support.

This post walks through when and why you'd still reach for Json.NET in a .NET 10 ASP.NET Core app, and exactly how to wire it up.

## Why Json.NET Still Matters

Json.NET has been around since 2007. It shipped before .NET Core existed, accumulated a decade of features, and became the de facto JSON library for .NET. Microsoft built `System.Text.Json` to replace it — with better performance and a more security-conscious design — but "replace" doesn't mean "feature-for-feature compatible on day one."

There are things Json.NET still does that STJ either doesn't support or requires significant workaround effort:

- **LINQ to JSON** (`JObject`, `JArray`, `JToken`) for working with dynamic or unknown JSON shapes
- **JsonPath** queries for extracting nested values
- **`TypeNameHandling`** for polymorphic serialization (though see the security section before you use it)
- Rich ecosystem of community `JsonConverter` implementations with no STJ equivalents
- Extensive custom `[JsonProperty]` and `[JsonConverter]` annotations in existing models

If you have a large codebase built on Json.NET and need to ship on .NET 10, the pragmatic answer is: keep using it. The migration cost might not be worth it yet.

## When to Use Each

Here's a rough decision matrix:

**Stay on Json.NET if:**
- You use `JObject`/`JArray` extensively for dynamic payloads
- You rely on `TypeNameHandling` for polymorphic serialization (with a `SerializationBinder`)
- You have complex custom `JsonConverter` chains that would require significant rewrites
- Third-party libraries you depend on use Json.NET types in their APIs

**Consider moving to STJ if:**
- You're starting a new project
- Performance and memory usage are priorities (STJ with source generation is measurably faster)
- You want trim-safe builds and AOT compatibility
- Your serialization needs are relatively straightforward

STJ has closed a lot of the gap. `[JsonDerivedType]`, `[JsonRequired]`, `ReferenceHandler.Preserve`, and source generation are all available now. But if you need `JObject` or `JsonPath`, you're still reaching for Json.NET.

## Installation

Add the official integration package:

```bash
dotnet add package Microsoft.AspNetCore.Mvc.NewtonsoftJson
```

For .NET 10, this pulls in `Newtonsoft.Json >= 13.0.3`. The package targets `net10.0` natively — no compatibility shims needed.

## Configuration in Program.cs

Wire it up in your minimal hosting setup:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
    });

var app = builder.Build();
app.MapControllers();
app.Run();
```

`AddNewtonsoftJson()` replaces the STJ input/output formatters in the MVC pipeline with Json.NET formatters. Any controller endpoint decorated with `[FromBody]` or returning a `JsonResult` will use your configured settings.

A few things to know:

- `options.SerializerSettings` maps to `JsonSerializerSettings` — it's the same object you'd use with `JsonConvert.DefaultSettings`
- Configuring `AddNewtonsoftJson` does **not** change `JsonConvert.DefaultSettings` — those are independent
- Setting `ContractResolver` to `CamelCasePropertyNamesContractResolver` is common when your C# models use PascalCase but your API should return camelCase

## Practical Code Examples

### Model Binding with [FromBody]

Nothing special here — once Json.NET is registered, `[FromBody]` just works with it:

```csharp
[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    [HttpPost]
    public IActionResult Create([FromBody] Order order)
    {
        // order was deserialized using Json.NET
        return Ok(order);
    }
}

public class Order
{
    [JsonProperty("order_id")]
    public int OrderId { get; set; }

    [JsonProperty("customer")]
    public string Customer { get; set; } = string.Empty;
}
```

The `[JsonProperty]` attribute from `Newtonsoft.Json` controls the JSON key names. STJ's `[JsonPropertyName]` has no effect here — the two libraries ignore each other's attributes.

### Custom JsonConverter Registration

Register converters globally through `SerializerSettings.Converters`:

```csharp
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.Converters.Add(new StringEnumConverter());
        options.SerializerSettings.Converters.Add(new IsoDateTimeConverter
        {
            DateTimeFormat = "yyyy-MM-dd"
        });
    });
```

With `StringEnumConverter` in place, enums serialize as strings instead of integers across all controller responses.

### LINQ to JSON for Dynamic Payloads

Sometimes you don't know the shape of the incoming JSON ahead of time. `JObject` handles this nicely:

```csharp
[HttpPost("webhook")]
public IActionResult HandleWebhook([FromBody] JObject payload)
{
    var eventType = payload["event"]?.Value<string>();
    var userId = payload["data"]?["user_id"]?.Value<int>();

    // JsonPath query for nested data
    var email = payload.SelectToken("$.data.contact.email")?.Value<string>();

    return Ok(new { eventType, userId, email });
}
```

`JObject` gives you a dynamic view of the JSON without needing a concrete type. `SelectToken` with JsonPath syntax (`$.data.contact.email`) lets you query nested structures without writing a chain of null checks.

### Using JsonResult with Explicit Settings

If you need different settings for a specific response, pass `JsonSerializerSettings` directly:

```csharp
[HttpGet("export")]
public IActionResult Export()
{
    var data = GetData();
    var settings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include
    };
    return new JsonResult(data, settings);
}
```

This is useful when one endpoint needs human-readable output (for debugging or file exports) while the rest of the API uses compact formatting.

## Minimal APIs and Json.NET

Here's the catch: `AddNewtonsoftJson()` only affects the MVC/controller pipeline. Minimal API endpoints use STJ — always.

```csharp
// This endpoint uses STJ, regardless of AddNewtonsoftJson()
app.MapGet("/items", () => new Item { Name = "Widget" });
```

If you need Json.NET serialization in a minimal endpoint, you have to do it manually:

```csharp
app.MapPost("/process", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var payload = JsonConvert.DeserializeObject<MyPayload>(body);

    var result = Process(payload!);
    var json = JsonConvert.SerializeObject(result);
    return Results.Content(json, "application/json");
});
```

It works, but it's verbose. Alternatively, you can wrap this in a custom `IResult` implementation if you need it in multiple places.

For new code in Minimal APIs, it's usually easier to stay with STJ unless you specifically need a Json.NET feature.

## Security: The TypeNameHandling Warning

Before you use `TypeNameHandling.All`, stop and read this.

`TypeNameHandling.All` embeds .NET type names in the JSON output:

```json
{
  "$type": "MyApp.Models.Order, MyApp",
  "orderId": 123
}
```

On deserialization, Json.NET instantiates the type named in `$type`. If an attacker can control that value, they can cause arbitrary .NET types to be instantiated — and that's a remote code execution vector.

This is a well-documented CVE-class vulnerability pattern. The rule is simple:

**Never use `TypeNameHandling.All` (or `.Objects`, `.Arrays`, `.Auto`) with input you don't control.**

If you genuinely need type-based polymorphism, use it only with a `SerializationBinder` that explicitly allowlists the types you're willing to instantiate:

```csharp
options.SerializerSettings.TypeNameHandling = TypeNameHandling.Auto;
options.SerializerSettings.SerializationBinder = new KnownTypesBinder(
    typeof(OrderCreated),
    typeof(OrderShipped),
    typeof(OrderCancelled)
);
```

Also configure `MaxDepth` to guard against deeply nested JSON that could blow your stack:

```csharp
options.SerializerSettings.MaxDepth = 32;
```

If you're on a new project with no existing Json.NET investment, the security design of STJ — which intentionally omits `TypeNameHandling` — is one more reason to start there.

## Performance Trade-offs

STJ is faster. That's not a knock on Json.NET — it's just a consequence of STJ being designed from scratch for .NET's UTF-8 pipeline, with source generation available to eliminate runtime reflection entirely.

In practice, the difference matters most at high throughput. For most apps, the serialization cost is noise compared to database queries and network round-trips. But if you're building something where JSON throughput is actually the bottleneck, STJ with source generation is the answer.

Json.NET uses reflection to inspect types at runtime, which adds allocation overhead on first use. It caches the results, so subsequent serializations of the same type are faster — but you pay that warm-up cost on startup or first request.

The rough hierarchy:
1. **STJ + source generation** — fastest, no reflection, AOT-safe
2. **STJ reflection-based** — fast, good for most cases
3. **Json.NET** — slower than STJ, still plenty fast for typical workloads

If performance is critical and you can migrate, STJ source generation eliminates reflection entirely. If you're staying on Json.NET, the performance is still very acceptable — millions of apps are running on it in production today.

## Troubleshooting Common Issues

**`JsonSerializationException: Self referencing loop detected`**
You have a circular reference in your object graph. Set `ReferenceLoopHandling.Ignore` in your settings to skip circular references, or `ReferenceLoopHandling.Serialize` with a `$ref` contract to preserve the structure.

**Attributes from the wrong library**
`[JsonPropertyName]` (STJ) and `[JsonProperty]` (Json.NET) look similar but are completely independent. Each library ignores the other's attributes. Check your using statements — if you've mixed up the namespaces, rename the attribute to the correct one.

**Swagger/OpenAPI documentation is broken**
Swashbuckle.AspNetCore uses STJ conventions by default. When you switch to `AddNewtonsoftJson()`, install the Newtonsoft adapter:

```bash
dotnet add package Swashbuckle.AspNetCore.Newtonsoft
```

Then register it:

```csharp
builder.Services.AddSwaggerGen();
builder.Services.AddSwaggerGenNewtonsoftSupport();
```

**SignalR isn't using Json.NET**
`AddNewtonsoftJson()` for MVC doesn't affect SignalR. SignalR has its own serialization pipeline. Configure it separately:

```csharp
builder.Services.AddSignalR()
    .AddNewtonsoftJsonProtocol(options =>
    {
        options.PayloadSerializerSettings.ContractResolver =
            new CamelCasePropertyNamesContractResolver();
    });
```

**Razor views: `Json.Serialize` still uses STJ**
`IJsonHelper` in Razor views is not part of the MVC formatter pipeline and doesn't pick up `AddNewtonsoftJson()`. If you need Json.NET serialization in a view, call `JsonConvert.SerializeObject` directly.

## Wrapping Up

Json.NET is opt-in since ASP.NET Core 3.0, but it's still a first-class option. The official `Microsoft.AspNetCore.Mvc.NewtonsoftJson` package targets `net10.0` natively, the ecosystem is mature, and the integration is straightforward — a package reference and a one-liner in `Program.cs`.

The reasons to stick with it are real: LINQ to JSON, JsonPath, rich custom converter ecosystems, and existing codebases that would cost more to migrate than to maintain. The reasons to consider moving to STJ are also real: better performance, source generation, AOT compatibility, and a security-first design that sidesteps the `TypeNameHandling` problem entirely.

Pick the tool that fits your actual situation. If you're on Json.NET and it's working, `AddNewtonsoftJson()` keeps you running on .NET 10 without drama. If you're starting fresh or performance-sensitive, STJ with source generation is the better starting point.
