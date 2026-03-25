---
title: Source Generators in C#
date: 2026-03-25
tags: csharp, dotnet, roslyn, language-features
image: source-generators-in-csharp.png
---

Source generators are one of those features that feel like magic once you start using them. Introduced in C# 9, they let the compiler generate code at build time — no reflection, no runtime overhead, just plain C# that gets compiled alongside your own code.

If you've ever wished you could eliminate repetitive boilerplate, source generators are your answer.

## What Are Source Generators

A source generator is a piece of code that runs *during compilation*. It inspects your existing code (via the Roslyn compiler API) and produces additional C# source files that get compiled into your assembly.

The key difference from runtime code generation: this all happens at compile time. The generated code ships with your binary, fully compiled and ready to go.

## Setting Up a Source Generator Project

Source generators live in their own class library project targeting `netstandard2.0`. That's a quirk of the tooling — the generator itself must target `netstandard2.0` even if your main project targets .NET 10.

```xml
<!-- MyGenerators/MyGenerators.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp"
                      Version="4.8.0"
                      PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers"
                      Version="3.3.4"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Then reference it from your main project with special metadata:

```xml
<ItemGroup>
  <ProjectReference Include="..\MyGenerators\MyGenerators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Writing Your First Generator

Let's build a simple generator that creates a `HelloWorld` class. It's not particularly useful, but it shows the mechanics clearly.

```csharp
using Microsoft.CodeAnalysis;

[Generator]
public class HelloWorldGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("HelloWorld.g.cs", """
                namespace Generated;

                public static class HelloWorld
                {
                    public static string Greet(string name)
                        => $"Hello, {name}!";
                }
                """);
        });
    }
}
```

The `[Generator]` attribute marks the class as a source generator. `IIncrementalGenerator` is the modern interface introduced in .NET 6 — it's much faster than the original `ISourceGenerator` because it only re-runs when relevant inputs change.

After building, you can use `Generated.HelloWorld.Greet("World")` in your project as if you wrote it yourself.

## A More Useful Example: ToString Generator

Here's where generators shine. Imagine automatically generating `ToString()` implementations for classes marked with a custom attribute.

First, define the attribute in the generator (so consumers don't need a separate package):

```csharp
[Generator]
public class ToStringGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Inject the marker attribute into every compilation
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("GenerateToStringAttribute.g.cs", """
                namespace MyGenerators;

                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class GenerateToStringAttribute : System.Attribute { }
                """);
        });

        // Find all classes marked with [GenerateToString]
        var classDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "MyGenerators.GenerateToStringAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.TargetNode)
            .Where(static c => c is not null);

        context.RegisterSourceOutput(classDeclarations, GenerateToString);
    }

    private static void GenerateToString(
        SourceProductionContext ctx,
        ClassDeclarationSyntax classDecl)
    {
        var className = classDecl.Identifier.Text;
        var namespaceName = GetNamespace(classDecl);

        var properties = classDecl.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(p => p.Identifier.Text)
            .ToList();

        var propList = string.Join(", ",
            properties.Select(p => $"{p}: {{{p}}}"));

        var source = $$"""
            namespace {{namespaceName}};

            partial class {{className}}
            {
                public override string ToString()
                    => $"{{className}} { {{propList}} }";
            }
            """;

        ctx.AddSource($"{className}.ToString.g.cs", source);
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var ns = node.Ancestors()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault()
            ?.Name.ToString();

        return ns ?? "Global";
    }
}
```

Now in your main project, you just do this:

```csharp
using MyGenerators;

[GenerateToString]
public partial class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

// Somewhere in your code:
var p = new Person { Name = "Alice", Age = 30 };
Console.WriteLine(p); // Person { Name: Alice, Age: 30 }
```

The `partial` keyword is important — it lets the generator add methods to your existing class from a separate generated file.

## Viewing Generated Code

You can inspect generated files by adding this to your `.csproj`:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Generated files appear under `obj/Generated/`. It's really helpful for debugging when something's not working as expected.

## Incremental Generators vs Original ISourceGenerator

If you're reading older tutorials, you'll see `ISourceGenerator`. Don't use it for new generators. `IIncrementalGenerator` is faster because it caches results and only re-runs when inputs change. This makes IDE performance much better — no more sluggish IntelliSense while the generator churns.

The `ForAttributeWithMetadataName` helper is particularly powerful. It efficiently finds all types decorated with a specific attribute without scanning every node in the syntax tree.

## When to Reach for Source Generators

Source generators are great for:

- **Eliminating boilerplate** — `ToString`, `Equals`, serialization code
- **Avoiding reflection** — JSON serialization (`System.Text.Json` uses them), DI containers
- **Compile-time validation** — catching configuration errors before runtime
- **Protocol implementations** — gRPC stubs, strongly typed string resources

They're overkill for simple utilities or one-off helpers. The setup cost is real, so make sure you'll get enough mileage out of the generator to justify it.

## Wrapping Up

Source generators bridge the gap between hand-written boilerplate and fragile runtime reflection. Once you get past the initial project setup, writing a generator feels a lot like writing any other Roslyn-based tool — you're just walking the syntax tree and emitting strings.

The incremental API makes them fast enough to run on every keystroke in the IDE, which means your generated code stays up to date without slowing down your workflow. Give them a try next time you find yourself writing the same class over and over.
