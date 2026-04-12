---
title: Beautiful .NET Consoles with Spectre.Console
date: 2026-04-12
tags: dotnet, csharp, cli, tutorial
image: beautiful-dotnet-consoles-with-spectre-console.png
---

If you've ever shipped a .NET command-line tool and felt embarrassed by its plain, colourless output, Spectre.Console is the library you've been waiting for. It brings Python's beloved `Rich` library to .NET — tables, progress bars, interactive prompts, and richly-coloured text with just a few lines of code.

It's a [.NET Foundation](https://dotnetfoundation.org) project, MIT licensed, and supports everything from .NET Standard 2.0 through .NET 10. The latest stable release is **0.55.0** (April 2026), actively maintained by Patrik Svensson, Phil Scott, Nils Andresen, and Cédric Luthi.

## Installing

```bash
dotnet add package Spectre.Console
```

Your first styled line of output:

```csharp
AnsiConsole.MarkupLine("[bold green]Hello, World![/]");
```

That's it. Bold green text in any ANSI-capable terminal.

## The Markup Language

Spectre.Console uses an inline tag syntax borrowed directly from Rich. Tags are square brackets — `[bold red]text[/]` — and they nest cleanly.

```csharp
AnsiConsole.MarkupLine("[bold]Bold[/], [italic]italic[/], [underline]underlined[/].");
AnsiConsole.MarkupLine("[red]red[/], [#ff8800]orange hex[/], [rgb(100,200,50)]custom rgb[/].");
AnsiConsole.MarkupLine("[link=https://spectreconsole.net]Spectre.Console website[/]");
```

Supported colour formats: named colours, hex codes (`#rrggbb`), and RGB triples. The library automatically **detects terminal capabilities** and gracefully downgrades colour depth — from 24-bit through 8-bit, 4-bit, 3-bit, down to no colour at all — so your output looks good everywhere.

When you're inserting dynamic content that might contain brackets, use `MarkupLineInterpolated()` instead. It automatically escapes interpolated values:

```csharp
var userInput = "[bold]this would break markup[/]";
AnsiConsole.MarkupLineInterpolated($"User said: {userInput}");
// renders: User said: [bold]this would break markup[/]
```

You can also escape manually with `Markup.Escape(str)` if you need to build markup strings dynamically.

## Widgets

This is where Spectre.Console earns its name. The widget API turns complex terminal output into a few readable lines.

### Tables

```csharp
var table = new Table();
table.AddColumn("[bold]Name[/]");
table.AddColumn(new TableColumn("[bold]Version[/]").RightAligned());
table.AddColumn("[bold]Status[/]");

table.AddRow("Spectre.Console", "0.55.0", "[green]✓ Installed[/]");
table.AddRow("Newtonsoft.Json", "13.0.3", "[yellow]↑ Update available[/]");
table.AddRow("Dapper", "2.1.66", "[green]✓ Installed[/]");

AnsiConsole.Write(table);
```

Tables support multiple border styles (`table.Border(TableBorder.Rounded)`), column alignment, header styling, and cell-level markup.

### Panels

Wrap content in a labelled box:

```csharp
AnsiConsole.Write(
    new Panel("[bold green]Build succeeded[/]\n3 projects, 0 warnings, 0 errors")
        .Header("Build Summary")
        .Padding(1, 0));
```

### Trees

```csharp
var root = new Tree("Solution")
    .Style(Style.Parse("green"));

var src = root.AddNode("[yellow]src/[/]");
src.AddNode("SiteGenerator.csproj");
src.AddNode("Program.cs");

var tests = root.AddNode("[yellow]tests/[/]");
tests.AddNode("SiteGenerator.Tests.csproj");

AnsiConsole.Write(root);
```

### Other Layout Widgets

- **Rule** — horizontal dividers: `AnsiConsole.Write(new Rule("[yellow]Section Title[/]"))`
- **FigletText** — large ASCII art banners: `AnsiConsole.Write(new FigletText("Hello").Color(Color.Blue))`
- **Calendar** — monthly calendar view with highlighted dates
- **Grid** and **Columns** — side-by-side layouts without a table structure

## Charts

Two chart types that render directly in the terminal.

### BarChart

```csharp
AnsiConsole.Write(new BarChart()
    .Width(60)
    .Label("[bold]Monthly Downloads[/]")
    .AddItem("Jan", 45_000, Color.Yellow)
    .AddItem("Feb", 52_000, Color.Green)
    .AddItem("Mar", 61_000, Color.Blue));
```

### BreakdownChart

Shows proportional slices of a total as a single coloured bar:

```csharp
AnsiConsole.Write(new BreakdownChart()
    .Width(60)
    .AddItem("C#", 58.4, Color.Blue)
    .AddItem("TypeScript", 19.2, Color.Yellow)
    .AddItem("Other", 22.4, Color.Grey));
```

You can even render images in the terminal via `CanvasImage` (powered by SixLabors.ImageSharp) and do pixel-level drawing with `Canvas`.

## Progress and Status

For long-running tasks, Spectre.Console's live rendering support keeps users informed without manual output management.

### Progress Bars

```csharp
await AnsiConsole.Progress()
    .StartAsync(async ctx =>
    {
        var build = ctx.AddTask("Building projects");
        var test = ctx.AddTask("Running tests");

        while (!ctx.IsFinished)
        {
            await Task.Delay(50);
            build.Increment(1);
            if (build.Value > 50) test.Increment(0.5);
        }
    });
```

Each task has its own bar with a label, percentage, and spinner. Multiple tasks render simultaneously.

### Status Spinner

For indeterminate work where progress isn't measurable:

```csharp
await AnsiConsole.Status()
    .StartAsync("Fetching packages...", async ctx =>
    {
        ctx.Status("Resolving dependencies...");
        await Task.Delay(1000);

        ctx.Status("Downloading NuGet packages...");
        await Task.Delay(2000);
    });
```

## Interactive Prompts

You can replace raw `Console.ReadLine()` calls with validated, navigable prompts.

### TextPrompt

```csharp
var name = AnsiConsole.Ask<string>("What's your [green]name[/]?");

var port = AnsiConsole.Prompt(
    new TextPrompt<int>("Port number?")
        .DefaultValue(8080)
        .Validate(p => p is > 0 and < 65536
            ? ValidationResult.Success()
            : ValidationResult.Error("Must be between 1 and 65535")));
```

### SelectionPrompt

```csharp
var env = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("Target [green]environment[/]?")
        .AddChoices("development", "staging", "production"));
```

Arrow keys navigate the list, Enter confirms. No parsing required.

### MultiSelectionPrompt

```csharp
var features = AnsiConsole.Prompt(
    new MultiSelectionPrompt<string>()
        .Title("Which [green]features[/] to enable?")
        .AddChoices("Dark Mode", "Telemetry", "Beta Features", "Auto-Update"));
```

Space to toggle, Enter to confirm — checkbox-style selection in the terminal.

## Spectre.Console.Cli

If you're building a CLI tool rather than just adding some polish to console output, `Spectre.Console.Cli` handles argument parsing with type safety:

```bash
dotnet add package Spectre.Console.Cli
```

Define your options as a class:

```csharp
public class DeploySettings : CommandSettings
{
    [CommandArgument(0, "<environment>")]
    public string Environment { get; init; } = "";

    [CommandOption("-v|--verbose")]
    public bool Verbose { get; init; }

    [CommandOption("--timeout <seconds>")]
    [DefaultValue(30)]
    public int TimeoutSeconds { get; init; }
}
```

Implement the command:

```csharp
public class DeployCommand : Command<DeploySettings>
{
    public override int Execute(CommandContext context, DeploySettings settings)
    {
        AnsiConsole.MarkupLine($"Deploying to [bold]{settings.Environment}[/]...");

        if (settings.Verbose)
            AnsiConsole.MarkupLine("[grey]Verbose mode enabled[/]");

        return 0;
    }
}
```

Wire it up:

```csharp
var app = new CommandApp<DeployCommand>();
return app.Run(args);
```

For multi-command tools:

```csharp
var app = new CommandApp();
app.Configure(config =>
{
    config.AddCommand<DeployCommand>("deploy");
    config.AddCommand<BuildCommand>("build");
    config.AddCommand<TestCommand>("test");
});
return app.Run(args);
```

Help text is generated automatically from your attribute definitions. Dependency injection via `Microsoft.Extensions.DependencyInjection` works with `.UseServiceProviderFactory()`. Async commands implement `AsyncCommand<TSettings>`.

## Unit Testing Console Output

Here's a feature most console libraries don't bother with: Spectre.Console is testable.

Add the testing package:

```bash
dotnet add package Spectre.Console.Testing
```

Inject `IAnsiConsole` instead of using `AnsiConsole` directly:

```csharp
public class BuildReporter(IAnsiConsole console)
{
    public void ReportSuccess(string project)
    {
        console.MarkupLine($"[green]✓[/] {project} built successfully");
    }
}
```

In your tests:

```csharp
[Fact]
public void ReportSuccess_WritesGreenCheckmark()
{
    var console = new TestConsole();
    var reporter = new BuildReporter(console);

    reporter.ReportSuccess("MyProject");

    Assert.Contains("MyProject built successfully", console.Output);
}
```

`TestConsole` captures everything written to it. You can also queue simulated user input for testing prompts, making interactive CLI tools fully unit-testable without spawning processes.

## Real-World Adoption

Spectre.Console has substantial real-world traction. Cake (the .NET build automation tool), ABP Framework, DiscordChatExporter, Quartz.NET, Microsoft Aspire, Hot Chocolate, and docfx all depend on it. There are 288 NuGet packages and 167 GitHub repositories that list it as a dependency.

A Serilog sink is available too — `Serilog.Sinks.SpectreConsole` — so you can route structured log output through Spectre's markup engine.

The [examples repository](https://github.com/spectreconsoles/examples) has runnable samples for every feature if you prefer to learn by running code.

## Wrapping Up

Spectre.Console covers the full range of console needs — from a single styled line of output to a full multi-command CLI with argument parsing, DI, and unit tests. The markup language is learnable in minutes, the widget API handles the complex layout work for you, and the `TestConsole` means you don't have to sacrifice testability for a polished terminal experience.

If you're building any .NET tool that humans will run from a terminal, it's worth the `dotnet add package` call.
