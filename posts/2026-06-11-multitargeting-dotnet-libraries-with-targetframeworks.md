---
title: Multi-Targeting .NET Libraries Without Headaches
date: 2026-06-11
tags: csharp, dotnet, libraries, architecture
image: multitargeting-dotnet-libraries-with-targetframeworks.png
---

Shipping a reusable .NET library gets interesting the moment consumers are on different runtimes. Some teams are on `net8.0`, others still need `netstandard2.0`, and you don't want separate projects for each.

Good news: multi-targeting is built in. You can compile one project for multiple frameworks and keep a clean API surface.

## Start with `TargetFrameworks`

In your `.csproj`, switch from `TargetFramework` to `TargetFrameworks`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

That single change tells the SDK to build your library twice — once per framework.

## Keep one API, vary internals when needed

Most of your code can stay framework-agnostic. When you do need runtime-specific behavior, use conditional compilation in small, isolated spots.

```csharp
public static class Clock
{
    public static DateTimeOffset UtcNow()
    {
#if NET8_0_OR_GREATER
        return TimeProvider.System.GetUtcNow();
#else
        return DateTimeOffset.UtcNow;
#endif
    }
}
```

This keeps callers on a single method while you optimize implementation per target.

## Use target-specific package references

Sometimes dependencies differ by framework. Put conditions directly on package references:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
  <PackageReference Include="System.Text.Json" Version="8.0.5" />
</ItemGroup>

<ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
</ItemGroup>
```

You can do the same with source files when a platform-specific implementation is cleaner than `#if` blocks.

## Example: framework-aware async disposal

`IAsyncDisposable` support is common now, but it's not always available in older targets without extra references. Here's a practical pattern that stays safe across targets:

```csharp
public sealed class BufferedWriter : IDisposable
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
    , IAsyncDisposable
#endif
{
    private readonly Stream _stream;

    public BufferedWriter(Stream stream) => _stream = stream;

    public void Dispose() => _stream.Dispose();

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
    public ValueTask DisposeAsync() => _stream.DisposeAsync();
#endif
}
```

Consumers on newer frameworks get `await using`, while older consumers still have reliable `Dispose()`.

## Test each target explicitly

Multi-targeting only helps if every target is green. In CI, run tests per framework so breakages are obvious:

```bash
dotnet test -f net8.0
dotnet test -f netstandard2.0
```

If your test project can't target every framework (common with `netstandard` libraries), create focused test projects or use compatibility smoke tests that compile against each target.

## When multi-targeting is worth it

It's usually worth the complexity when:

- You publish a shared package used by multiple apps
- You need modern APIs for new runtimes but still support older ones
- You want one NuGet package instead of maintaining parallel branches

If all consumers are already on one modern runtime, single-targeting is still the simplest choice.

## Final thought

Multi-targeting looks scary at first, but most of the time it's just a `.csproj` setting plus a few well-placed conditions. Keep the public API stable, isolate framework-specific code, and test each target like it's its own product.

That's how you stay compatible without drowning in maintenance.
