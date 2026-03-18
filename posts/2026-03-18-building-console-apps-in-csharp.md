---
title: Building Console Apps in C#
date: 2026-03-18
tags: dotnet, csharp, console, cli
image: building-console-apps-in-csharp.png
---

Console apps get underestimated. They're not glamorous, but they're often the fastest way to ship a useful tool — a migration script, a data importer, a dev utility that you'll run a hundred times. C# is a great fit for them, and the ecosystem has matured to the point where building a proper CLI takes very little ceremony.

This post covers the basics, manual argument parsing, and then two NuGet packages that handle the heavy lifting when your tool grows up.

## The Entry Point

A modern .NET console app is as minimal as this:

```csharp
Console.WriteLine("Hello, world!");
```

That's it. Top-level statements (introduced in C# 9) mean there's no `Main` method boilerplate unless you want it. The compiler generates it for you.

If you need the traditional style — maybe you're on an older codebase or prefer the explicit structure — it looks like this:

```csharp
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, world!");
    }
}
```

Both are fine. Top-level statements are the default for `dotnet new console` and the style I'd use for anything new.

## Reading and Writing

`Console.WriteLine` writes a line. `Console.Write` writes without a newline. `Console.ReadLine` reads a line of input. These three cover most basic I/O.

```csharp
Console.Write("What's your name? ");
var name = Console.ReadLine();
Console.WriteLine($"Hello, {name}!");
```

For coloured output, set `Console.ForegroundColor` before writing, then reset it:

```csharp
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("Build succeeded.");
Console.ResetColor();
```

It works, but it's manual and gets old fast. We'll fix that with Spectre.Console later.

## Command-Line Arguments

The `args` array (or `Environment.GetCommandLineArgs()` if you're using the traditional `Main` style) contains whatever the caller passed in.

```csharp
// dotnet run -- Alice 42
var name = args[0];    // "Alice"
var age  = args[1];    // "42" — it's always a string
```

Simple tools can get away with positional arguments like this. You index into `args`, parse types manually, and add a bit of bounds checking:

```csharp
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: mytool <name> <age>");
    return 1;
}

var name = args[0];

if (!int.TryParse(args[1], out var age))
{
    Console.Error.WriteLine("Age must be an integer.");
    return 1;
}

Console.WriteLine($"{name} is {age} years old.");
return 0;
```

Note the `return 1` for error cases — that's the exit code, and callers (scripts, CI pipelines) use it to detect failures. Returning a non-zero code from `Main` signals something went wrong.

## Manual Parsing Gets Messy Fast

Positional arguments are fine for two or three values. Once you add optional flags, boolean switches, or subcommands, the manual approach becomes a maintenance burden. Parsing `--verbose`, `--output path/to/file`, and `--format json|csv` by hand means a lot of `args[i] == "--verbose"` checks and off-by-one errors.

You also have to write your own `--help` output, handle invalid input gracefully, and keep everything consistent. It's not hard — it's just tedious, and there are packages that do all of it better.

## System.CommandLine

`System.CommandLine` is Microsoft's official CLI framework. It's been in preview for a while, but it's stable and widely used. Install it with:

```bash
dotnet add package System.CommandLine
```

Here's a simple tool that accepts a name and an optional `--shout` flag:

```csharp
using System.CommandLine;

var nameArg = new Argument<string>("name", "The name to greet");
var shoutOpt = new Option<bool>("--shout", "Print in uppercase");

var rootCommand = new RootCommand("A friendly greeter");
rootCommand.AddArgument(nameArg);
rootCommand.AddOption(shoutOpt);

rootCommand.SetHandler((name, shout) =>
{
    var greeting = $"Hello, {name}!";
    Console.WriteLine(shout ? greeting.ToUpper() : greeting);
}, nameArg, shoutOpt);

return await rootCommand.InvokeAsync(args);
```

Run it:

```bash
dotnet run -- Alice           # Hello, Alice!
dotnet run -- Alice --shout   # HELLO, ALICE!
dotnet run -- --help          # auto-generated help text
```

The `--help` output is generated automatically from the descriptions you provide. Argument types are validated, and type conversion (strings to `int`, `FileInfo`, enums, etc.) is handled for you.

For tools with multiple subcommands — think `git commit`, `git push` — you add `Command` objects to the root command:

```csharp
var addCommand = new Command("add", "Add an item");
var removeCommand = new Command("remove", "Remove an item");

rootCommand.AddCommand(addCommand);
rootCommand.AddCommand(removeCommand);
```

Each subcommand gets its own arguments, options, and handler. The framework routes the call based on what the user typed.

`System.CommandLine` is the right choice when you need structured, predictable CLI behaviour and want something that Microsoft will keep aligned with .NET tooling conventions.

## Spectre.Console

`Spectre.Console` is a different kind of library. It's focused on making terminal output beautiful and interactive. Install it with:

```bash
dotnet add package Spectre.Console
```

The most basic upgrade from `Console.WriteLine` is markup support:

```csharp
using Spectre.Console;

AnsiConsole.MarkupLine("[green]Build succeeded.[/]");
AnsiConsole.MarkupLine("[bold red]Error:[/] file not found.");
```

No more manually setting and resetting `Console.ForegroundColor`. Markup is inline and composable.

Tables are trivially easy:

```csharp
var table = new Table();
table.AddColumn("Package");
table.AddColumn("Version");
table.AddColumn("Status");

table.AddRow("Markdig", "0.39.0", "[green]OK[/]");
table.AddRow("Newtonsoft.Json", "13.0.3", "[green]OK[/]");
table.AddRow("Serilog", "4.1.0", "[yellow]Outdated[/]");

AnsiConsole.Write(table);
```

Progress bars and spinners are built in — no threading gymnastics required:

```csharp
await AnsiConsole.Progress()
    .StartAsync(async ctx =>
    {
        var task = ctx.AddTask("[green]Downloading packages[/]");

        while (!ctx.IsFinished)
        {
            await Task.Delay(100);
            task.Increment(10);
        }
    });
```

Spectre.Console also has `AnsiConsole.Ask\<T\>` for prompts and `SelectionPrompt\<T\>` for interactive menus:

```csharp
var name = AnsiConsole.Ask<string>("What's your [blue]name[/]?");

var env = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("Which [green]environment[/]?")
        .AddChoices("dev", "staging", "production"));

AnsiConsole.MarkupLine($"Deploying to [bold]{env}[/]...");
```

The user gets arrow-key navigation and instant selection — the kind of UX you'd expect from polished CLI tools, not from a console app you threw together in an afternoon.

Spectre.Console also includes its own CLI framework (`Spectre.Console.Cli`) if you want argument parsing and rich output in one package. It uses a command pattern with attribute-based option binding, which some people prefer over the fluent style of `System.CommandLine`.

## Choosing Between Them

They're not mutually exclusive — you can use `System.CommandLine` for argument parsing and `Spectre.Console` for output in the same project. That's actually a common combination.

If you're building a straightforward tool with a clear command structure, `System.CommandLine` is the natural fit. It's the framework .NET's own tooling was heading toward and integrates well with how .NET handles hosting and DI.

If you're building something where the terminal experience matters — dashboards, interactive prompts, formatted reports — reach for `Spectre.Console`. It's one of the most polished NuGet packages in the .NET ecosystem.

## Wrapping Up

Console apps in C# don't need to be bare-bones. You can go from a basic `WriteLine` loop to a fully featured CLI with proper argument parsing, auto-generated help text, and rich terminal output without writing much code from scratch.

The short version:
- Top-level statements keep entry points minimal — `dotnet new console` defaults to them
- `args` gives you raw command-line input; fine for simple tools, brittle for anything bigger
- Return meaningful exit codes (`0` for success, non-zero for failure) — pipelines depend on them
- `System.CommandLine` handles argument parsing, routing, and `--help` generation with minimal ceremony
- `Spectre.Console` handles colours, tables, progress bars, and interactive prompts with a clean markup API
- Use them together — argument parsing from one, rich output from the other — when your tool deserves it
