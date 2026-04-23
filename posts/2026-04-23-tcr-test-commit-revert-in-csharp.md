---
title: TCR: Test && Commit || Revert in C#
date: 2026-04-23
tags: csharp, dotnet, testing, tdd, xunit
image: tcr-test-commit-revert-in-csharp.png
---

If you've read the [TDD post](test-driven-development-in-csharp.html) on this blog, you know the red-green-refactor loop: write a failing test, make it pass, clean up. TCR takes that idea and cranks it up to eleven. Instead of a discipline you maintain yourself, TCR makes the machine enforce it — automatically committing your code when tests pass and silently deleting it when they don't.

It sounds extreme. It kind of is. That's the point.

## What Is TCR?

TCR stands for `test && commit || revert`. It's a workflow invented by Kent Beck in 2018 at a code camp in Oslo hosted by Iterate. The "revert" twist was actually suggested by Oddmund Strømme — Beck initially hated it. Then he tried it and wrote:

> "I'm not arguing for TCR nor even describing its trade-offs. I'm saying it seems like it shouldn't work at all, it does, and I hope you try it."

The mechanic is simple. After every small change, you run:

```bash
dotnet test && git add -A && git commit -m "working" || git checkout .
```

Tests pass? Committed. Tests fail? Your changes are gone, instantly.

There's no "I'll fix it in a moment." The moment is now, or it's back to square one.

## Why Kent Beck Created It

The original motivation was an experiment Beck called "Limbo on the Cheap" — exploring how software teams could scale collaboration when every commit on the shared branch is always green. If a commit breaks tests, it never lands. TCR is the local enforcement mechanism for that idea.

Beck is the creator of XP (Extreme Programming), TDD, and a co-author of the Agile Manifesto. His whole career has been about amplifying good practices: make feedback fast, keep steps small, stay green. TCR is XP taken to its logical extreme.

TDD says: the red state is temporary and intentional. TCR says: there is no red state. If you write a test that fails and can't immediately fix it, the test gets deleted along with everything else.

## The Three TCR Increments

TCR pushes you toward three patterns, each progressively harder:

1. **Add a test and pass it (even with a fake implementation)** — write the simplest test, then return a hardcoded value. It's green. Commit.
2. **Replace the fake with something real (while staying green)** — now triangulate with a second test case that the hardcoded value can't satisfy. Generalise.
3. **Make hard changes easy** — before tackling a complex change, introduce the helpers, abstractions, or structure you need, each step committed while green. Then make the change.

"Cheating" with a hardcoded return isn't laziness — it's a tool. It proves the test infrastructure works and gives you a committed baseline before you tackle the harder step.

## Setting Up TCR for a C# Project

You need a .NET project with tests. Any framework works — xUnit, NUnit, MSTest. The TCR script itself is tiny.

### Bash (macOS/Linux)

```bash
#!/bin/bash
dotnet test && git add -A && git commit -m "working" || git checkout .
```

Save this as `tcr.sh`, run `chmod +x tcr.sh`, and invoke it after each small change.

### PowerShell (Windows)

```powershell
dotnet test
if ($LASTEXITCODE -eq 0) {
    git add -A
    git commit -m "working"
} else {
    git checkout .
}
```

Save this as `tcr.ps1` and run it from your project root.

### Watch Mode

If you want TCR to trigger automatically on file save, you can wire it up with `dotnet watch`:

```bash
dotnet watch test
```

`dotnet watch test` reruns tests on file change but doesn't commit or revert — you'd still invoke the commit/revert step manually or via a file watcher script. For kata practice, running the script manually after each intentional change tends to feel more natural.

## Practising TCR with FizzBuzz

FizzBuzz is a good first kata for TCR because you already know the solution, which lets you focus on the *process* rather than the problem.

Start with a new xUnit project:

```bash
dotnet new xunit -n FizzBuzzTcr
cd FizzBuzzTcr
git init
git add -A
git commit -m "initial"
```

### Step 1 — the simplest test

Write the tiniest possible test:

```csharp
public class FizzBuzzTests
{
    [Fact]
    public void Returns_number_as_string_for_1()
    {
        Assert.Equal("1", FizzBuzz.Get(1));
    }
}
```

Run TCR. It fails — `FizzBuzz` doesn't exist. Your test disappears. That stings, but it teaches you something: *create the class before writing the test*, or at least get it compiling first. Start again:

```csharp
public static class FizzBuzz
{
    public static string Get(int n) => "";
}
```

Commit that skeleton (tests pass because... well, the single test doesn't exist yet). Now add the test again:

```csharp
[Fact]
public void Returns_number_as_string_for_1()
{
    Assert.Equal("1", FizzBuzz.Get(1));
}
```

Run TCR. Still red — returns `""` not `"1"`. Your test disappears again. This is the key insight: you need to make it pass *before* running TCR. So make it pass immediately with a fake:

```csharp
public static string Get(int n) => "1";
```

Run TCR. Green. Committed. You now have a baseline.

### Step 2 — triangulate away from the fake

The hardcoded `"1"` won't survive a second test:

```csharp
[Fact]
public void Returns_number_as_string_for_2()
{
    Assert.Equal("2", FizzBuzz.Get(2));
}
```

Run TCR. Fails — `Get(2)` returns `"1"`. The new test is gone. Generalise first:

```csharp
public static string Get(int n) => n.ToString();
```

Now add the second test again. Green. Committed. Both cases handled.

### Step 3 — add Fizz

```csharp
[Fact]
public void Returns_Fizz_for_3()
{
    Assert.Equal("Fizz", FizzBuzz.Get(3));
}
```

Make it pass before running TCR:

```csharp
public static string Get(int n)
{
    if (n % 3 == 0) return "Fizz";
    return n.ToString();
}
```

Green. Committed. Continue with Buzz (5), FizzBuzz (15), and the rest — each one a tiny step, each committed the moment it's green.

## TCR vs TDD

| | TDD | TCR |
|---|---|---|
| Red state allowed | Yes (temporarily) | No — code deleted on failure |
| Commit frequency | You decide | Every passing test run |
| Step size | Guided by discipline | Enforced by machine |
| Learning curve | Moderate | Steep but fast |
| Good for production | Yes | Katas and learning |

TCR doesn't replace TDD — it's a teaching tool. After a few sessions of losing code to the revert, you naturally develop an instinct for smaller steps that carries back into your regular TDD practice.

## The Sunk Cost Effect

One underrated benefit of TCR: it kills the sunk cost fallacy. In normal development, when tests are red after 20 minutes of work, you think "I can't throw this away, I've already invested so much." So you push through, stacking more complexity on a shaky foundation.

TCR makes the choice for you. The code is gone. Start smaller. The forced restart feels brutal, but it consistently leads to cleaner approaches than the "almost working" code you would have kept.

## Criticisms and Practical Limits

TCR isn't without critics. GeePaw Hill described it as "a useful but incomplete provocation" — it highlights the value of small steps without being practical as a daily workflow.

The main limitations:

- **Slow test suites break the loop.** If `dotnet test` takes 30 seconds, running it after every three-line change feels punishing. TCR demands a fast test suite — ideally under 5 seconds.
- **It doesn't handle exploratory work well.** When you're not sure what you're building, committing every green state produces noisy history and doesn't help.
- **The revert is blunt.** It deletes *everything*, including your test, even if the test itself was fine and only the implementation was wrong. With practice, you learn to commit the test separately before implementing.
- **Not suited for legacy systems.** If you're working in code with a 3-minute build time and flaky tests, TCR will make your day miserable.

Use it for katas. Use it when learning a new language. Use it when you want to break the habit of taking steps that are too large. Then bring the instincts back to your normal workflow.

## Conclusion

TCR is a provocation, not a prescription. Kent Beck didn't introduce it as the new way to build software — he introduced it because it *shouldn't* work, and the fact that it does is instructive.

The lesson isn't "always use TCR." The lesson is: **if your code gets deleted every time tests fail, you'll figure out how to take smaller steps pretty quickly.** That instinct — baby steps, always green, commit often — is valuable in any workflow.

Try it once with FizzBuzz or the String Calculator kata. Lose some code. Start smaller. You might be surprised what you learn.

Key takeaways:

- **TCR = `test && commit || revert`**: tests pass, commit; tests fail, revert. No manual steps.
- Kent Beck created it in 2018 as an experiment in always-green collaboration, not as a daily workflow.
- The three increments — fake it, triangulate, abstract — are the same patterns TDD teaches, just enforced harder.
- A fast test suite is non-negotiable. Slow tests kill the feedback loop.
- Use TCR as a learning tool for katas. The instincts it builds transfer to regular TDD.
