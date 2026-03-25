---
title: Newtonsoft.Json vs System.Text.Json
date: 2026-03-26
tags: dotnet, csharp, aspnetcore, json, serialization
image: newtonsoft-json-vs-system-text-json.png
---

If you've worked with .NET for a while, you've almost certainly used Newtonsoft.Json. It's been the go-to JSON library for over a decade. But since .NET Core 3.0, Microsoft ships its own serializer — `System.Text.Json` — right in the box. So which one should you reach for?

Let's look at both, compare them honestly, and show how to swap the default serializer in ASP.NET Core when you need to.

## System.Text.Json — The New Default

`System.Text.Json` is Microsoft's built-in JSON library. It's fast, allocation-friendly, and designed around modern .NET patterns. It ships with the runtime — no NuGet package needed.

```csharp
using System.Text.Json;

var person = new Person("Alice", 30);
string json = JsonSerializer.Serialize(person);
// {"Name":"Alice","Age":30}

var deserialized = JsonSerializer.Deserialize<Person>(json);
```

It defaults to PascalCase property names and is strict about unknown properties. You can configure it with `JsonSerializerOptions`:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

string json = JsonSerializer.Serialize(person, options);
```

For most greenfield projects, `System.Text.Json` is the right call. It's fast, well-maintained, and you don't need an extra dependency.

## Newtonsoft.Json — The Battle-Hardened Option

Newtonsoft.Json (Json.NET) has been around since 2006. It's got a richer feature set, more flexible converters, and handles a lot of edge cases that `System.Text.Json` still doesn't cover out of the box.

```bash
dotnet add package Newtonsoft.Json
```

```csharp
using Newtonsoft.Json;

var person = new Person("Alice", 30);
string json = JsonConvert.SerializeObject(person);

var deserialized = JsonConvert.DeserializeObject<Person>(json);
```

Things Newtonsoft handles well that `System.Text.Json` struggles with (or didn't until recently):

- **Circular references** — `ReferenceLoopHandling.Ignore` is one setting
- **Dynamic and `object` types** — deserializes to `JObject`/`JArray`
- **`[JsonConstructor]`** on non-public constructors
- **Polymorphic serialization** with `TypeNameHandling`
- **Flexible camelCase** by default when you want it

## Swapping the Serializer in ASP.NET Core

ASP.NET Core uses `System.Text.Json` by default since .NET 5. To switch to Newtonsoft.Json, add the `Microsoft.AspNetCore.Mvc.NewtonsoftJson` package:

```bash
dotnet add package Microsoft.AspNetCore.Mvc.NewtonsoftJson
```

Then call `AddNewtonsoftJson()` when registering MVC services:

```csharp
// Program.cs (.NET 6+)
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ContractResolver =
            new CamelCasePropertyNamesContractResolver();
        options.SerializerSettings.NullValueHandling =
            NullValueHandling.Ignore;
        options.SerializerSettings.ReferenceLoopHandling =
            ReferenceLoopHandling.Ignore;
    });

var app = builder.Build();
```

That's it — your controllers, `JsonResult` responses, and model binding all go through Newtonsoft now.

## Configuring Minimal APIs

If you're using minimal APIs (the `app.MapGet(...)` style), the swap is a bit different. Minimal APIs use `System.Text.Json` independently of the MVC pipeline. You configure it via `JsonOptions`:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Still System.Text.Json for minimal APIs
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
```

Newtonsoft.Json doesn't plug into minimal APIs directly. If you need Newtonsoft there too, you'll need to call `JsonConvert.SerializeObject()` manually and return a `Results.Content(json, "application/json")`.

## Comparing the Key Differences

Here's a quick reference:

| Feature | System.Text.Json | Newtonsoft.Json |
|---|---|---|
| NuGet required | No | Yes |
| Performance | Faster | Slower |
| Circular references | Via `ReferenceHandler` | `ReferenceLoopHandling` |
| Dynamic objects | Limited | Full `JObject`/`JArray` support |
| Default casing | PascalCase | Configurable |
| Polymorphism | `.NET 7+` | `TypeNameHandling` |
| LINQ to JSON | No | Yes |

## When to Stick With Newtonsoft.Json

The main reasons to reach for Newtonsoft in a new project:

1. **Migrating a large existing codebase** that's heavily dependent on Json.NET behaviour
2. **Third-party libraries** that return `JObject`/`JToken` and you need to manipulate them
3. **Complex inheritance hierarchies** where you need `TypeNameHandling`
4. **Legacy API contracts** with quirky serialization requirements that `System.Text.Json` can't match without custom converters

## When to Use System.Text.Json

For anything new, default to `System.Text.Json`. It's faster, built in, and keeps getting better with each .NET release. .NET 7 added native support for polymorphic serialization via `[JsonPolymorphic]`, and .NET 8 improved source generation support dramatically.

```csharp
// .NET 7+ polymorphism, no Newtonsoft needed
[JsonPolymorphic]
[JsonDerivedType(typeof(Dog), "dog")]
[JsonDerivedType(typeof(Cat), "cat")]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public class Dog : Animal { public string Breed { get; set; } = ""; }
public class Cat : Animal { public bool IsIndoor { get; set; } }
```

## A Quick Note on Source Generation

`System.Text.Json` supports source-generated serializers in .NET 6+. This removes reflection entirely at runtime — great for AOT and trimming scenarios:

```csharp
[JsonSerializable(typeof(Person))]
[JsonSerializable(typeof(List<Person>))]
internal partial class AppJsonContext : JsonSerializerContext { }

// Use the generated context
var json = JsonSerializer.Serialize(person, AppJsonContext.Default.Person);
```

Newtonsoft.Json doesn't support source generation — it's entirely reflection-based. For AOT-published apps or NativeAOT, `System.Text.Json` with source generation is the only viable option.

## Wrapping Up

Both serializers are production-ready. `System.Text.Json` is the right default for new projects — it's fast, built-in, and keeps improving. Newtonsoft.Json is still a solid choice when you're working with legacy code, need its richer feature set, or are integrating with libraries that depend on it.

If you're starting fresh with ASP.NET Core on .NET 10, give `System.Text.Json` a real go before reaching for Newtonsoft. You might be surprised how far it's come.
