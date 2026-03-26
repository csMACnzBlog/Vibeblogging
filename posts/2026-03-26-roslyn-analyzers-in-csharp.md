---
title: Roslyn Analyzers in C#
date: 2026-03-26
tags: dotnet, csharp, roslyn, code-analysis
image: roslyn-analyzers-in-csharp.png
---

Yesterday we looked at Source Generators — code that runs at compile time to produce new C# files. Roslyn Analyzers are the other half of that story. Where generators *create* code, analyzers *inspect* it and report problems. They're how tools like StyleCop, the nullable reference type warnings, and your favourite IDE hints actually work.

Writing your own analyzer lets you enforce project-specific rules automatically. No more "please don't use `DateTime.Now` in this codebase" comments on every PR.

## How Analyzers Hook Into the Compiler

Roslyn exposes the full compiler pipeline as a set of public APIs. When you write an analyzer, you register interest in specific syntax nodes or symbols — things like method declarations, object creations, or property accesses. The compiler calls into your analyzer as it processes the code, and you report diagnostics back.

Those diagnostics show up exactly like compiler warnings and errors: in the IDE, in build output, and in CI. You can even make them errors to block a build.

## Project Setup

Analyzers live in a `netstandard2.0` class library — same restriction as source generators, for the same reasons. The project file looks like this:

```xml
<!-- MyAnalyzers/MyAnalyzers.csproj -->
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

Reference it from the consuming project using the `Analyzer` output type:

```xml
<ItemGroup>
  <ProjectReference Include="..\MyAnalyzers\MyAnalyzers.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

The `ReferenceOutputAssembly="false"` is important — you don't want the analyzer DLL as a runtime dependency.

## Writing a Simple Analyzer

Let's enforce a common .NET convention: async methods should end with the `Async` suffix. It's a simple rule, but useful in a team where people forget.

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncMethodNamingAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "MYLIB001",
        title: "Async method name should end with 'Async'",
        messageFormat: "Async method '{0}' should be renamed to '{0}Async'",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Async methods should follow the Async suffix convention.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeMethod,
            SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        var isAsync = method.Modifiers.Any(SyntaxKind.AsyncKeyword);
        if (!isAsync)
            return;

        var name = method.Identifier.Text;
        if (name.EndsWith("Async"))
            return;

        var diagnostic = Diagnostic.Create(
            Rule,
            method.Identifier.GetLocation(),
            name);

        context.ReportDiagnostic(diagnostic);
    }
}
```

The `[DiagnosticAnalyzer]` attribute identifies this class to the compiler. The `DiagnosticDescriptor` defines the rule — give it a unique ID with your library's prefix so it doesn't clash with other analyzers. The `Initialize` method is where you register what you care about; here we're asking for a callback on every method declaration.

The two lines at the start of `Initialize` are boilerplate you should always include. `ConfigureGeneratedCodeAnalysis` stops the analyzer running on generated code (like the `.g.cs` files from yesterday's source generators). `EnableConcurrentExecution` lets Roslyn call your analyzer on multiple threads — make sure you're not touching shared mutable state.

## Adding a Code Fix

Diagnostics are useful. Code fixes that automatically correct the problem are *delightful*. They show up as the lightbulb suggestions in Visual Studio and Rider.

Code fixes live in a separate class that references the same diagnostic ID:

```csharp
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public class AsyncMethodNamingCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(AsyncMethodNamingAnalyzer.Rule.Id);

    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        var diagnostic = context.Diagnostics[0];
        var node = root?.FindNode(diagnostic.Location.SourceSpan);
        if (node is not MethodDeclarationSyntax method)
            return;

        var oldName = method.Identifier.Text;
        var newName = oldName + "Async";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Rename to '{newName}'",
                createChangedSolution: ct =>
                    RenameAsync(context.Document, method, newName, ct),
                equivalenceKey: nameof(AsyncMethodNamingCodeFix)),
            diagnostic);
    }

    private static async Task<Solution> RenameAsync(
        Document document,
        MethodDeclarationSyntax method,
        string newName,
        CancellationToken ct)
    {
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var symbol = semanticModel?.GetDeclaredSymbol(method, ct);
        if (symbol is null)
            return document.Project.Solution;

        return await Renamer.RenameSymbolAsync(
            document.Project.Solution, symbol, new SymbolRenameOptions(), newName, ct)
            .ConfigureAwait(false);
    }
}
```

The `Renamer.RenameSymbolAsync` call is the nice part — it renames the method *and* all call sites across the solution. You don't have to do a text find-and-replace yourself.

## Testing Your Analyzer

The `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` packages give you a nice test harness. Add these to a test project:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit" Version="1.1.2" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.XUnit" Version="1.1.2" />
```

Then write tests like this:

```csharp
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Verifier = Microsoft.CodeAnalysis.CSharp.Testing.XUnit
    .AnalyzerVerifier<AsyncMethodNamingAnalyzer>;

public class AsyncMethodNamingAnalyzerTests
{
    [Fact]
    public async Task MethodWithoutAsyncSuffix_ReportsDiagnostic()
    {
        var code = """
            public class MyClass
            {
                public async System.Threading.Tasks.Task {|MYLIB001:FetchData|}()
                {
                    await System.Threading.Tasks.Task.Delay(1);
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(code);
    }

    [Fact]
    public async Task MethodWithAsyncSuffix_NoDiagnostic()
    {
        var code = """
            public class MyClass
            {
                public async System.Threading.Tasks.Task FetchDataAsync()
                {
                    await System.Threading.Tasks.Task.Delay(1);
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(code);
    }
}
```

The `{|MYLIB001:FetchData|}` syntax marks the exact span where you expect the diagnostic. The test framework compares the actual diagnostic location against what you marked — if they don't match, the test fails. It's a bit unusual at first, but it makes tests very precise.

## Distributing via NuGet

The cleanest way to share an analyzer is as a NuGet package. There's some specific structure required so NuGet knows the DLL is an analyzer, not a regular library:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <PackageId>MyCompany.MyAnalyzers</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <None Include="$(OutputPath)\$(AssemblyName).dll"
          Pack="true"
          PackagePath="analyzers/dotnet/cs"
          Visible="false" />
  </ItemGroup>
</Project>
```

The `analyzers/dotnet/cs` path is the magic incantation. NuGet looks there specifically for analyzer DLLs when restoring packages. The `IncludeBuildOutput="false"` stops the DLL from being added as a regular assembly reference.

Once published, consumers just add the NuGet reference and the analyzer starts running. No project file changes needed on their end, which is a lovely developer experience.

## When Should You Write One

Custom analyzers make sense when you have a rule that:

- Can't be expressed with existing EditorConfig settings
- Keeps coming up in code reviews
- Has a clear, automatable fix
- Isn't just a style preference (those belong in `.editorconfig`)

The classic examples are things like "don't call `DateTime.Now` in this service layer — use `IDateTimeProvider` instead" or "every public API method needs a `CancellationToken` parameter". Rules that are easy to state but easy to forget.

They're probably overkill for a rule that only applies in one file, or a rule so subtle that even you'd have trouble defining it precisely.

## Wrapping Up

Roslyn analyzers sit at a sweet spot: they catch problems at the exact moment the code is written, before a PR exists, before CI runs, before anyone has to leave a code review comment. And with a code fix attached, fixing the problem is a single keystroke.

The setup is genuinely similar to source generators — same project structure, same dependency on `Microsoft.CodeAnalysis.CSharp`. If you've already got a source generator project, adding an analyzer to it is pretty natural. Give it a go next time a pattern comes up in code review for the third time.
