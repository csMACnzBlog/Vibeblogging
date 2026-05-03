---
title: Custom JSON Converters in System.Text.Json
date: 2026-05-03
tags: dotnet, csharp, json, tutorial
image: custom-json-converters-in-system-text-json.png
---

`System.Text.Json` handles most serialization scenarios out of the box, but every sufficiently complex application eventually hits a case where the default behaviour just isn't right. Maybe you've got a legacy API that sends dates as Unix timestamps, or enums as strings, or you want to deserialize polymorphic types without any `$type` metadata. That's where custom converters come in.

Writing your own `JsonConverter<T>` is straightforward once you understand the pattern. This post walks through the common cases.

## The Converter Base Class

All custom converters inherit from `JsonConverter<T>`:

```csharp
public class MyConverter : JsonConverter<MyType>
{
    public override MyType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Deserialize here
    }

    public override void Write(
        Utf8JsonWriter writer,
        MyType value,
        JsonSerializerOptions options)
    {
        // Serialize here
    }
}
```

`Utf8JsonReader` is a forward-only, low-allocation reader — you call `reader.GetString()`, `reader.GetInt32()`, etc. to pull values out. `Utf8JsonWriter` has symmetric `WriteString`, `WriteNumber`, and friends for producing JSON output.

Once you have a converter, you register it either globally via `JsonSerializerOptions` or locally with a `[JsonConverter]` attribute.

## Example: Unix Timestamps

Say you have an API that represents dates as Unix epoch seconds. The default serializer expects ISO 8601 strings. You need a converter:

```csharp
public class UnixEpochDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var seconds = reader.GetInt64();
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        var epoch = new DateTimeOffset(value, TimeSpan.Zero).ToUnixTimeSeconds();
        writer.WriteNumberValue(epoch);
    }
}
```

Register it globally so all `DateTime` properties use it:

```csharp
var options = new JsonSerializerOptions();
options.Converters.Add(new UnixEpochDateTimeConverter());

var json = JsonSerializer.Serialize(DateTime.UtcNow, options);
// "1746230400" instead of "2026-05-03T00:00:00Z"
```

Or annotate individual properties when you only need it in one place:

```csharp
public class Event
{
    public string Name { get; set; } = "";

    [JsonConverter(typeof(UnixEpochDateTimeConverter))]
    public DateTime OccurredAt { get; set; }
}
```

The attribute takes priority over any global registration.

## Example: Enums as Strings

The default serializer writes enum values as integers. `JsonStringEnumConverter` handles the straightforward case:

```csharp
var options = new JsonSerializerOptions
{
    Converters = { new JsonStringEnumConverter() }
};

var json = JsonSerializer.Serialize(Status.Active, options);
// "Active" instead of 1
```

But what if the API uses a naming convention different from your C# enum? You can customise the naming policy:

```csharp
var options = new JsonSerializerOptions
{
    Converters =
    {
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
    }
};

// Status.NotStarted => "not_started"
```

For complete control — say, the API sends abbreviations you can't derive from the name — write a converter directly:

```csharp
public class OrderStatusConverter : JsonConverter<OrderStatus>
{
    private static readonly Dictionary<string, OrderStatus> _map = new()
    {
        ["NEW"] = OrderStatus.Pending,
        ["PRO"] = OrderStatus.Processing,
        ["CMP"] = OrderStatus.Completed,
        ["CXL"] = OrderStatus.Cancelled,
    };

    private static readonly Dictionary<OrderStatus, string> _reverse =
        _map.ToDictionary(kv => kv.Value, kv => kv.Key);

    public override OrderStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var code = reader.GetString()
            ?? throw new JsonException("Expected a string for OrderStatus");

        if (_map.TryGetValue(code, out var status))
            return status;

        throw new JsonException($"Unknown OrderStatus code: {code}");
    }

    public override void Write(
        Utf8JsonWriter writer,
        OrderStatus value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(_reverse[value]);
    }
}
```

## Example: Polymorphic Deserialization

Polymorphic JSON is where the default serializer really shows its limits. Given this payload:

```json
[
  { "type": "email", "to": "alice@example.com", "subject": "Hello" },
  { "type": "sms",   "to": "+64211234567",      "body": "Hi" }
]
```

You want to deserialize into an `INotification[]` with concrete `EmailNotification` and `SmsNotification` instances. With a custom converter:

```csharp
public abstract class Notification
{
    public abstract string Type { get; }
}

public class EmailNotification : Notification
{
    public override string Type => "email";
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
}

public class SmsNotification : Notification
{
    public override string Type => "sms";
    public string To { get; set; } = "";
    public string Body { get; set; } = "";
}

public class NotificationConverter : JsonConverter<Notification>
{
    public override Notification Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var type = root.GetProperty("type").GetString()
            ?? throw new JsonException("Missing 'type' property");

        return type switch
        {
            "email" => root.Deserialize<EmailNotification>(options)!,
            "sms"   => root.Deserialize<SmsNotification>(options)!,
            _       => throw new JsonException($"Unknown notification type: {type}")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        Notification value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, (object)value, value.GetType(), options);
    }
}
```

The key trick is using `JsonDocument.ParseValue` to buffer the JSON object, read the discriminator property, then call back into `Deserialize<T>` with the right type. The cast to `(object)` in `Write` forces runtime dispatch so the derived type's properties are included.

Register it and the conversion is transparent to callers:

```csharp
var options = new JsonSerializerOptions();
options.Converters.Add(new NotificationConverter());

var notifications = JsonSerializer.Deserialize<Notification[]>(json, options);
// [EmailNotification, SmsNotification]
```

> **Note**: .NET 7+ introduced `[JsonDerivedType]` and `JsonPolymorphismOptions` as a built-in alternative. If you control both ends of the wire and can add a `$type` field, the attribute-based approach is simpler. The converter above is better when you're adapting to an existing API format you can't change.

## Example: Strongly Typed IDs

If you're using strongly typed IDs to avoid primitive obsession, you need converters so they serialize as plain values rather than objects:

```csharp
public record UserId(Guid Value);

public class UserIdConverter : JsonConverter<UserId>
{
    public override UserId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetGuid();
        return new UserId(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        UserId value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
```

Without this, `UserId` serializes as `{"value":"..."}`. With it, you get the bare GUID string — exactly what an API consumer expects.

For a project with lots of ID types, a converter factory avoids repeating yourself for each one:

```csharp
public class StronglyTypedIdConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Match any class with a single Guid property named Value
        if (typeToConvert.IsClass)
        {
            var props = typeToConvert.GetProperties();
            return props.Length == 1
                && props[0].Name == "Value"
                && props[0].PropertyType == typeof(Guid);
        }
        return false;
    }

    public override JsonConverter? CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var converterType = typeof(StronglyTypedIdConverter<>)
            .MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

public class StronglyTypedIdConverter<T> : JsonConverter<T>
{
    private static readonly Func<Guid, T> _create;
    private static readonly Func<T, Guid> _getValue;

    static StronglyTypedIdConverter()
    {
        var ctor = typeof(T).GetConstructor([typeof(Guid)])!;
        _create = guid => (T)ctor.Invoke([guid]);

        var prop = typeof(T).GetProperty("Value")!;
        _getValue = id => (Guid)prop.GetValue(id)!;
    }

    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => _create(reader.GetGuid());

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(_getValue(value));
}
```

Register the factory once and it handles every matching type automatically.

## Registering with ASP.NET Core

In an ASP.NET Core app you configure the JSON options during startup:

```csharp
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.Converters.Add(new UnixEpochDateTimeConverter());
    opts.SerializerOptions.Converters.Add(new NotificationConverter());
    opts.SerializerOptions.Converters.Add(new StronglyTypedIdConverterFactory());
});
```

For MVC or Razor Pages, it's the same but via `AddJsonOptions`:

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new UnixEpochDateTimeConverter());
    });
```

## A Few Things to Watch Out For

**Don't call `JsonSerializer.Serialize` inside `Read`, or `Deserialize` inside `Write`, with the same options that contain your converter** — you'll get a stack overflow from infinite recursion. Use `JsonDocument.ParseValue` as an intermediate step instead, as shown in the polymorphism example.

**Throw `JsonException` for invalid input**, not `ArgumentException` or anything else. The serializer catches `JsonException` and wraps it with position information to help callers debug malformed payloads.

**Options instances are expensive to create** — the serializer caches reflection metadata on first use. Create your options once (as a static field or a DI singleton) and reuse them throughout the application.

## Wrapping Up

Custom converters are the escape hatch `System.Text.Json` gives you for everything the defaults don't handle. The pattern is consistent: inherit `JsonConverter<T>`, implement `Read` and `Write`, register in options. Once you've written a couple, it becomes muscle memory.

The trickier cases — polymorphism, converter factories — add a bit more ceremony, but the core loop stays the same. And because `Utf8JsonReader` and `Utf8JsonWriter` are low-allocation by design, your custom converters inherit the performance characteristics of the underlying library without any extra work.
