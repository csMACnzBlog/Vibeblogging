---
title: goto in C#: Almost Always a Mistake
date: 2026-05-25
tags: csharp, dotnet, clean-code, architecture
image: goto-in-csharp-almost-always-a-mistake.png
---

The `goto` statement in C# is one of those features that makes experienced developers visibly uncomfortable when they see it in a code review. There's a reason for that.

Let's talk about why `goto` exists, why you should almost never use it, and the two narrow cases where it's actually fine.

## A Bit of History

In 1968, Edsger Dijkstra wrote a short letter to the editors of *Communications of the ACM* titled "Go To Statement Considered Harmful." It's one of the most influential papers in computer science.

Dijkstra's argument was simple: `goto` makes it impossible to reason about the progress of a program. When you can jump to any labelled line at any point, understanding what state your code is in becomes extremely difficult. He wasn't being dramatic — he was right.

Structured programming (using loops, conditionals, and function calls instead of jumps) came out of that era and became the foundation of modern software development. `goto` stuck around in some languages as a legacy feature — C# included — but its use has become a code smell.

## Why goto Is a Problem

The core issue is that `goto` breaks the linearity of control flow. Code normally flows top to bottom, with predictable branching via `if`, `while`, `for`, and so on. When a `goto` jumps into the middle of a block, or back up to an earlier label, that mental model collapses.

Here's a contrived but illustrative example:

```csharp
int x = 0;

start:
if (x < 5)
{
    Console.WriteLine(x);
    x++;
    goto start;
}
```

This is just a `while` loop. But now you have to track the label, the condition, the jump — and figure out yourself that it terminates. A `while` loop makes that obvious.

```csharp
while (x < 5)
{
    Console.WriteLine(x);
    x++;
}
```

The second version is self-documenting. The first is not.

More practically, `goto` creates code that's hard to:

- **Debug** — stepping through jumps in a debugger is disorienting
- **Test** — multiple entry and exit paths are hard to isolate
- **Reason about** — program state becomes difficult to trace
- **Refactor** — labels are tightly coupled to their targets

## The Better Alternatives

Almost every place you'd consider `goto`, there's a cleaner option.

**Breaking out of nested loops?** Extract the inner logic into a method and use `return`:

```csharp
// Instead of this
for (int i = 0; i < rows; i++)
{
    for (int j = 0; j < cols; j++)
    {
        if (matrix[i][j] == target)
            goto found;
    }
}
found:
Console.WriteLine("Found it");

// Do this
bool FindInMatrix(int[][] matrix, int target)
{
    for (int i = 0; i < matrix.Length; i++)
        for (int j = 0; j < matrix[i].Length; j++)
            if (matrix[i][j] == target)
                return true;
    return false;
}
```

**Error handling with early exits?** Use early returns or exceptions:

```csharp
// Not this
if (order == null) goto end;
if (!order.IsValid) goto end;
ProcessOrder(order);
end:
return;

// This
if (order == null) return;
if (!order.IsValid) return;
ProcessOrder(order);
```

**Looping with conditions?** Use `while`, `do/while`, `for`, or `foreach`. There's always a fit.

## The Two Acceptable Uses

There are exactly two places in C# where `goto` is genuinely reasonable.

### 1. `goto case` and `goto default` in switch statements

C# doesn't allow implicit fall-through in switch cases (unlike C). But sometimes you want one case to fall through to another explicitly. `goto case` handles this cleanly:

```csharp
switch (coffeeType)
{
    case "espresso":
        price = 3.00m;
        goto case "standard";
    case "americano":
        price = 2.50m;
        goto case "standard";
    case "standard":
        price += taxRate;
        break;
    default:
        price = 2.00m;
        break;
}
```

This is explicit, scoped to the switch construct, and structured. Microsoft's official documentation uses this exact pattern. It's not great code (you'd probably use a helper method), but it's not harmful either.

### 2. Breaking out of deeply nested loops (rarely)

If you genuinely have deeply nested loops and extracting them into a method isn't practical, `goto` is a defensible option:

```csharp
for (int i = 0; i < data.Length; i++)
{
    for (int j = 0; j < data[i].Length; j++)
    {
        for (int k = 0; k < data[i][j].Length; k++)
        {
            if (data[i][j][k] == sentinel)
                goto done;
        }
    }
}
done:
// continue here
```

Is this ideal? No. The existence of triple-nested loops is already a design issue worth addressing. But as emergency escapes go, this is the lesser evil compared to boolean flag soup.

## The Compiler Uses goto, and That's Different

Here's something interesting: the C# compiler generates `goto` extensively in its output for `async`/`await` code. State machines for async methods use labelled jumps internally.

That's fine. Compiler-generated code has different constraints — it's not read by humans, it's optimised for execution, and it's generated systematically. The fact that the compiler uses `goto` is not an argument for writing it yourself; it's evidence that `goto` is a tool for code generators, not developers.

## The Mental Checklist

Before reaching for `goto`, ask yourself:

1. Can I extract this into a method and use `return`?
2. Can I restructure the loops or conditions to avoid the jump?
3. Am I in a `switch` statement using `goto case`?
4. Is this triple-nested logic that genuinely resists refactoring?

If you answered yes to question 1 or 2 — which you almost always will — then `goto` is the wrong choice.

## Wrapping Up

`goto` isn't evil because of some arbitrary rule. It's problematic because it undermines the structured, predictable control flow that makes code readable and maintainable. Dijkstra figured this out in 1968 and the industry has agreed ever since.

Use `goto case` in switches when you need explicit fall-through. Use `goto` to escape deeply nested loops only when method extraction truly isn't feasible. Otherwise, treat it as a code smell and refactor.

If you spot a `goto` in a code review that doesn't fit those two cases, question it. It'll almost always turn out there's a better way.
