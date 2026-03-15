---
title: What's New in .NET 10 and C# 14
date: 2026-03-15
tags: dotnet, csharp, performance, aspnetcore, release
image: whats-new-in-dotnet-10-csharp-14.png
---

.NET 10 shipped in November 2025, and it's a Long-Term Support release — the kind of milestone that signals "yes, it's time to migrate." If you're still on .NET 6 or .NET 8, this is your green light. The .NET 10.0.1 patch (KB5072928, December 9 2025) sealed the deal, and Microsoft's committed to supporting it until November 2028.

But LTS status isn't the headline story. The real news is what's *in* the release: a genuinely useful set of C# 14 language features, some impressive JIT and GC work under the hood, and framework improvements across Blazor, ASP.NET Core, EF Core, and MAUI. Let's walk through what actually matters and why.

## The LTS Case for Migrating Now

There's a practical argument for upgrading that has nothing to do with new features: teams retargeting to `net10.0` are reporting immediate performance gains and reduced memory usage with minimal code changes. The runtime improvements are just *there*, baked in — you don't have to opt into them.

If you're on .NET 8 (also LTS, supported until November 2026), you've got time. But .NET 6 reached end of life in November 2024, so if that's where you are, upgrading isn't optional anymore. .NET 10 is the natural next LTS target.

The migration story is generally smooth. Microsoft has published breaking changes documentation, but most projects retarget cleanly. The bigger blockers tend to be dependencies lagging behind on NuGet, not the framework itself.

## C# 14: Engineering Ergonomics

C# 14 doesn't reinvent the language. It fills in gaps and removes friction. Here are the features that'll actually change how you write code day to day.

### Extension Members

This is the headline feature. C# has had extension methods for years, but you couldn't add extension properties, operators, or static members. C# 14 changes that.

The new syntax uses an explicit `extension` block:

```csharp
public static class MoneyExtensions
{
    extension(Money m)
    {
        public decimal InDollars => m.Amount / 100m;
        
        public static Money Zero => new Money(0);
        
        public static Money operator +(Money a, Money b)
            => new Money(a.Amount + b.Amount);
    }
}
```

This matters most when you're working with types you don't own — third-party libraries, generated types, or framework types where inheritance isn't an option. You can now build what you might call "domain algebra": extend a `Money` struct with `+` and `-` operators, add a `.DisplayName` property to an enum, or add factory methods to a sealed class.

The extension block groups related members logically, which is a readability improvement over the old pattern of scattering static extension methods across a class.

### The `field` Keyword

This one bridges a gap that's existed since auto-properties were introduced. If you wanted to add validation or transformation inside a property accessor, you had to drop down to a full backing field:

```csharp
// Before: had to write the backing field manually
private string _name = string.Empty;
public string Name
{
    get => _name;
    set => _name = value?.Trim() ?? string.Empty;
}
```

With the `field` keyword, the compiler generates the backing field for you, but you still get access to it:

```csharp
// C# 14: field refers to the compiler-generated backing field
public string Name
{
    get;
    set => field = value?.Trim() ?? string.Empty;
}
```

It's a small thing, but it removes a category of boilerplate. You get the validation logic you want without the manual field declaration. Combine it with a getter-only property and you can express clamping, normalisation, and null-guarding patterns very cleanly.

### Null-Conditional Assignment

You've probably written this pattern dozens of times:

```csharp
if (logger != null)
{
    logger.Value = newValue;
}
```

Or the slightly tighter version:

```csharp
logger?.SetValue(newValue);  // method call is fine
// but assignment? That didn't work before
```

C# 14 adds `?.=` for exactly this case:

```csharp
logger?.Value = newValue;  // only assigns if logger is not null
```

The right-hand side expression is only evaluated if the target isn't null. That matters when the value has side effects — you're not just skipping the assignment, you're skipping the evaluation entirely.

### Natural Span Programming

Span\<T\> and ReadOnlySpan\<T\> have been the performance story in .NET for a while, but working with them has always required some ceremony around conversions. C# 14 adds improved implicit conversions that make span-based code feel more natural:

```csharp
// These now work without explicit conversion
ReadOnlySpan<char> greeting = "Hello, world";  // string → ReadOnlySpan<char>
Span<int> numbers = [1, 2, 3, 4, 5];           // collection expression → Span<int>

// Pass directly to span-accepting methods
ProcessData("input text");          // no explicit AsSpan() call needed
```

This reduces the friction of writing allocation-free code. The performance characteristics were always there — now the ergonomics match.

## The Runtime: What's Actually Faster

.NET 10's runtime improvements are substantial, and they're the kind of thing where you see the benefit without doing anything.

### JIT: Block Reordering as a Travelling Salesman Problem

The JIT now models code block placement using a 3-opt heuristic — the same class of algorithm used in route optimisation problems. The goal is keeping "hot" code (frequently executed paths) physically adjacent in memory, which improves CPU cache efficiency.

In practice, this means tight loops and hot paths get better cache behaviour without any changes to your code. The JIT figures out the optimal ordering at compile time.

### Physical Promotion for Structs

Before .NET 10, when the JIT worked with struct members, it would often store the struct to memory and then load individual fields from it — even when the whole operation could have happened in registers.

Physical Promotion changes this: the JIT can now place individual struct fields directly into CPU registers for the duration of a method, skipping the memory round-trips entirely. For data-heavy code that passes structs around a lot — financial calculations, geometry, anything with value types — this can be a significant win.

### DATAS: Smarter Memory Management

DATAS (Dynamic Adaptation to Application Sizes) is a new GC mode that's enabled by default in .NET 10. The key behaviour: when your application's workload is light, the GC aggressively returns memory to the OS rather than holding onto heap space speculatively.

For containerised microservices, this is a real improvement. A service that handles bursts of traffic and then sits quiet will now actually release that memory, letting the container scheduler see accurate resource usage. Previously, heap memory grabbed during a burst would often stay claimed until the process restarted.

### Array Interface Devirtualization

When you call methods like `foreach`, LINQ, or explicitly use `IEnumerable<T>` over an array, the JIT has to handle the virtual dispatch to the interface implementation. In .NET 10, the JIT can devirtualize these calls — it knows at JIT time that the underlying type is an array, so it can inline the implementation directly.

This removes the performance penalty of coding to abstractions when the concrete type is an array. Your `IEnumerable<T>` method signature doesn't cost you anything extra if the caller passes an array.

## Framework Highlights

### Blazor 10: Smaller and Faster

The `blazor.web.js` runtime has been reduced by 76% — from roughly 183 KB to around 43 KB. This was achieved through pre-compression and asset fingerprinting improvements. Combined with .NET 10's static asset serving improvements, Blazor apps now have near-instant startup times compared to earlier versions.

If you dismissed Blazor based on the initial download size, it's worth revisiting.

### ASP.NET Core 10

Three additions stand out:

**Built-in validation for Minimal APIs.** Calling `builder.Services.AddValidation()` wires up automatic request validation using `DataAnnotations` attributes or custom `IValidator<T>` implementations. No more manually calling `ModelState.IsValid` for every endpoint.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddValidation();  // wire up validation

var app = builder.Build();

app.MapPost("/orders", (Order order) =>
{
    // order is automatically validated before this runs
    return Results.Created($"/orders/{order.Id}", order);
});
```

**Server-Sent Events support.** Native `TypedResults.ServerSentEvents()` in Minimal APIs makes streaming updates from server to client straightforward, without reaching for a library.

**OpenAPI 3.1 and YAML.** Full compatibility with the latest OpenAPI spec, including YAML output. Your generated API docs will now work with tooling that requires OpenAPI 3.1.

### EF Core 10

Two additions that will matter if you're doing AI-adjacent work:

**Vector type support for SQL Server.** EF Core 10 supports the SQL Server `vector` column type directly, with distance functions (`CosineSimilarity`, `NegativeDotProduct`) that map to SQL. This means you can do similarity search — the foundation of RAG patterns and semantic search — without leaving LINQ.

```csharp
// Find documents closest to a query embedding
var results = await context.Documents
    .OrderBy(d => EF.Functions.VectorDistance("cosine", d.Embedding, queryVector))
    .Take(10)
    .ToListAsync();
```

**Native LeftJoin and RightJoin.** LINQ finally has explicit left and right join operators rather than forcing you to use `GroupJoin` and `SelectMany` with a workaround. This makes join-heavy queries much more readable.

### MAUI 10

The headline number: a new XAML source generator that's 1,000% faster at view inflation and uses 99% less memory during debugging. If you've worked with MAUI or Xamarin and suffered through slow debug cycles, this is a meaningful quality-of-life improvement.

## File-Based Apps: C# as a Script

This one's a shift in how you can use the language. .NET 10 supports running a single `.cs` file directly:

```bash
dotnet run main.cs
```

You can reference NuGet packages using `#` directives at the top of the file:

```csharp
#!/usr/bin/dotnet run
#:package Newtonsoft.Json@13.0.3

using Newtonsoft.Json;

var data = new { message = "hello from a script" };
Console.WriteLine(JsonConvert.SerializeObject(data, Formatting.Indented));
```

This positions C# as a viable scripting language for automation, quick tools, and exploration — territory that's been mostly Python and PowerShell. You don't need a project file, a solution, or any scaffolding. Just a `.cs` file and a package reference if you need one.

## Security: Post-Quantum Cryptography

.NET 10 ships with built-in implementations of post-quantum cryptography algorithms — specifically ML-KEM (for key encapsulation) and ML-DSA (for digital signatures). These are NIST-standardised algorithms designed to resist attacks from quantum computers.

```csharp
// ML-KEM key generation
using var kem = MLKem.GenerateKey(MLKemAlgorithm.MLKem768);
var (encapsulatedKey, sharedSecret) = kem.Encapsulate();
```

You don't need to deploy this today — quantum computers capable of breaking current cryptography don't exist yet. But for systems that encrypt data today that needs to remain confidential for decades, the migration window has opened. .NET 10 gives you the tools to start that work.

## A Note on Tooling

C# 14 features require Visual Studio 2026 (or the corresponding `dotnet` SDK version). If you're on VS 2022, you'll get the .NET 10 runtime improvements but won't be able to use the new language features until you update your IDE.

Also worth knowing: .NET 10 enforces CET (Control-flow Enforcement Technology) at build time on Windows. If you're building on Windows 10, you may hit compatibility issues — CET is a hardware security feature that requires Windows 11 for full toolchain support. Windows 11 is the recommended build environment.

## Is It Time to Upgrade?

If you're on .NET 6: yes, immediately — it's out of support.

If you're on .NET 8: you've got runway until November 2026, but .NET 10 gives you better performance for free and a cleaner path forward. A measured migration now beats a rushed one later.

If you're on .NET 9: .NET 9 isn't LTS (it reaches end-of-life in May 2026), so .NET 10 is the upgrade you should be planning for.

The migration story from .NET 8 to .NET 10 is generally smooth — retarget your projects, update your package references, and run your tests. Most of the effort is in packages and tooling, not the framework itself. The C# 14 features are additive and don't break existing code.

The case for upgrading is strong: LTS support through 2028, meaningful performance improvements with no code changes required, genuinely useful language features in C# 14, and significant framework improvements across the whole stack. It's a solid release.
