---
title: Native AOT in .NET 10
date: 2026-04-29
tags: dotnet, csharp, performance, tutorial
image: native-aot-in-dotnet.png
---

For most of .NET's history, your code ran with a JIT compiler warming things up at startup. That's fine for long-running services, but there are scenarios where you want a self-contained binary that starts instantly, uses minimal memory, and doesn't ship the .NET runtime at all. That's what Native AOT compilation gives you.

.NET 10 makes Native AOT more polished and more broadly applicable than ever. This post covers what it is, how to use it, what the tradeoffs are, and where it actually makes sense.

## What Native AOT Actually Is

With the default .NET runtime, your C# code compiles to IL (Intermediate Language). When the process starts, the JIT compiler translates IL to native machine code on demand — warming up as your code runs. The first call to a method is slower; subsequent calls use cached native code.

Native AOT skips all of that. At publish time, the entire application is compiled directly to native machine code for the target platform. The resulting binary:

- Has no JIT compiler
- Has no IL
- Has no .NET runtime dependency (it's fully self-contained)
- Starts in milliseconds
- Uses significantly less memory

The tradeoff is that compilation happens at publish time rather than runtime, so you lose some runtime flexibility — specifically around reflection and dynamic code generation.

## Enabling Native AOT

Add a single property to your `.csproj`:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

Then publish:

```bash
dotnet publish -r linux-x64 -c Release
```

That's it. The output is a single native binary with no external runtime dependency.

For a console app, this produces something like a 5–15 MB binary that starts in under 10 milliseconds. Compare that to a JIT-based app which might take 200–500 ms to reach steady state on first run.

## A Simple Example

Here's a complete console app that works perfectly with Native AOT:

```csharp
// Program.cs
var numbers = Enumerable.Range(1, 100).ToArray();

var sum = numbers.Sum();
var average = numbers.Average();
var evens = numbers.Where(n => n % 2 == 0).ToArray();

Console.WriteLine($"Sum: {sum}, Average: {average}, Even count: {evens.Length}");
```

LINQ works fine. Regular method calls work fine. Most of the BCL works fine. The things that don't work are the things that require runtime code generation.

## What Doesn't Work

The main limitation is anything that requires dynamic behaviour at runtime — things the AOT compiler can't reason about statically:

**Reflection** is the big one. Loading types by name, calling methods via `MethodInfo.Invoke`, or inspecting arbitrary assemblies won't work unless you annotate your code to tell the compiler what types it needs to preserve.

```csharp
// This won't work in AOT without annotations
var type = Type.GetType("MyApp.SomeService");
var instance = Activator.CreateInstance(type);
```

**`dynamic` keyword** is out entirely. It relies on runtime compilation that doesn't exist in an AOT binary.

**Emit and expression compilation** — `System.Reflection.Emit` and `Expression.Compile()` both generate IL at runtime. AOT has no JIT to run that IL.

The compiler warns you at publish time about potential AOT compatibility issues. Don't ignore those warnings.

## Source Generation to the Rescue

The answer to reflection in AOT is source generators. Instead of reflecting at runtime, you use a source generator to emit the code at build time.

The most common case is JSON serialisation. `System.Text.Json` has full AOT support via source generation:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

// Define a source-generated serialisation context
[JsonSerializable(typeof(WeatherForecast))]
[JsonSerializable(typeof(List<WeatherForecast>))]
public partial class AppJsonContext : JsonSerializerContext { }

// Use it explicitly
var forecast = new WeatherForecast(DateTime.UtcNow, 22, "Sunny");
var json = JsonSerializer.Serialize(forecast, AppJsonContext.Default.WeatherForecast);
var back = JsonSerializer.Deserialize<WeatherForecast>(json, AppJsonContext.Default.WeatherForecast);

record WeatherForecast(DateTime Date, int TemperatureC, string Summary);
```

The source generator inspects `WeatherForecast` at build time and generates fast, reflection-free serialisation code. No runtime reflection, no AOT issues.

## Native AOT with Minimal APIs

ASP.NET Core's minimal APIs support Native AOT as a first-class scenario. Create a new project with the template:

```bash
dotnet new webapiaot -n MyAotApi
```

The generated project wires up JSON source generation automatically:

```csharp
using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(
        0, AppJsonContext.Default);
});

var app = builder.Build();

app.MapGet("/forecasts", () =>
{
    return Enumerable.Range(1, 5).Select(i => new WeatherForecast(
        Date: DateTime.UtcNow.AddDays(i),
        TemperatureC: Random.Shared.Next(-10, 40),
        Summary: "Partly cloudy"));
});

app.Run();

record WeatherForecast(DateTime Date, int TemperatureC, string Summary);

[JsonSerializable(typeof(IEnumerable<WeatherForecast>))]
internal partial class AppJsonContext : JsonSerializerContext { }
```

`CreateSlimBuilder` is a key detail here. It's a cut-down version of `CreateBuilder` that excludes features incompatible with AOT (like parts of the configuration system that use reflection). The trade-off is less magic, more explicit setup.

Published AOT, this API starts in about 10–20 ms and uses around 15–25 MB of RAM. A typical JIT-based minimal API might take 300–500 ms to start and use 60–100 MB.

## Trim Analysis and Warnings

The AOT compiler uses a trimmer to remove code that's never called. It does this by tracing from entry points through every static call — anything not reachable gets cut. This is aggressive: it'll trim large chunks of the BCL if you don't use them.

When reflection is involved, the trimmer can't always determine what types are needed. That produces a warning:

```
warning IL2026: Using member 'System.Type.GetType(String)' which has
'RequiresUnreferencedCodeAttribute' can break functionality when trimming
application code.
```

You have a few ways to address this:

**The `[DynamicallyAccessedMembers]` attribute** — Annotate parameters and properties to tell the trimmer what reflection accesses are expected:

```csharp
public void RegisterService(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    Type serviceType)
{
    // The trimmer knows to preserve public constructors on serviceType
    var instance = Activator.CreateInstance(serviceType);
    _services[serviceType] = instance;
}
```

**The `[RequiresUnreferencedCode]` attribute** — Mark methods that use reflection as not AOT-safe:

```csharp
[RequiresUnreferencedCode("Uses reflection to load plugins")]
public void LoadPlugins(string path)
{
    foreach (var assembly in Directory.GetFiles(path, "*.dll"))
    {
        Assembly.LoadFrom(assembly); // fine here, warning propagates to callers
    }
}
```

**Source generators** — The long-term answer. Replace reflection-based code with generated code.

## When to Use Native AOT

Native AOT isn't for everything. Here's where it shines:

**CLI tools** — Nobody wants to wait for a JIT warmup when running a command-line tool. AOT gives you near-instant startup.

**Serverless functions** — Cold start time is a real cost on platforms like AWS Lambda or Azure Functions. AOT can cut cold starts from seconds to milliseconds.

**Containerised microservices** — Smaller images (no runtime dependency), faster scaling, lower memory baseline.

**Embedded or resource-constrained environments** — Environments where the .NET runtime doesn't fit.

For long-running services that have already warmed up, the JIT runtime is often *faster* because the JIT can apply runtime-specific optimisations (inlining based on actual call frequency, profile-guided optimisation). AOT's native code is compiled conservatively at build time.

## Checking AOT Compatibility

Before committing to AOT, you can audit your dependencies:

```bash
dotnet publish -r linux-x64 -c Release /p:PublishAot=true
```

The build output will list AOT compatibility warnings from your code and any NuGet packages. Libraries are increasingly annotating themselves for AOT compatibility — you'll see it marked on NuGet packages as "AOT compatible" or get explicit warnings when a package isn't.

For third-party libraries, check their release notes. Many popular packages (EF Core, SignalR, MediatR, and others) now have AOT-compatible paths or are working toward them.

## Wrapping Up

Native AOT is one of those features that doesn't fit every scenario, but when it fits, it fits perfectly. CLI tools, serverless functions, and microservices that need instant startup all benefit significantly.

The programming model change is real — you give up reflection-driven magic in exchange for fast startup and no runtime dependency. .NET 10 makes this easier with better tooling, clearer warnings, and more of the ecosystem annotated for AOT compatibility.

If you're building a new CLI tool or serverless function, it's worth enabling `PublishAot` from the start and keeping the warnings clean. Retrofitting AOT into an existing reflection-heavy app is significantly harder.
