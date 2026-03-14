---
title: Characterisation Tests in C#
date: 2026-03-14
tags: csharp, dotnet, testing, characterisation-tests
image: characterisation-tests-in-csharp.png
---

We've covered a lot of ground in this testing series. [Unit tests](unit-testing-in-csharp-with-xunit.html), [TDD](test-driven-development-in-csharp.html), [mocking](mocking-with-moq-in-csharp.html), [integration testing](integration-testing-in-aspnet-core.html), [property-based testing](property-based-testing-with-fscheck.html) — all of those assume you're working with code you understand, or code you're writing fresh. But what do you do when you inherit a 3,000-line class with no tests, no documentation, and a mandate to refactor it without breaking anything?

That's where characterisation tests come in. They're sometimes called "golden master tests" or "approval tests," but the idea is the same: capture what the code *currently does*, not what it *should* do, so you have a safety net when you start moving things around.

## What Characterisation Tests Actually Are

A characterisation test doesn't verify correctness — it verifies *consistency*. You run the code, capture the output, and then assert that future runs produce the same output. The test characterises the existing behaviour, warts and all.

That distinction matters. If your legacy `InvoiceFormatter` has been generating slightly malformed XML for five years and downstream systems silently tolerate it, a characterisation test captures that malformed output as the expected value. You're not endorsing the bug; you're making sure you don't accidentally fix it during refactoring and break everything that depends on it.

Michael Feathers introduced the term in *Working Effectively with Legacy Code*, and the workflow is simple:
1. Run the code and observe what it produces.
2. Write a test that asserts that exact output.
3. Refactor safely — the test will fail if your refactoring changes observable behaviour.

## The Simplest Possible Approach

Suppose you inherit this method — no documentation, unclear intent:

```csharp
public class LegacyPricingEngine
{
    public decimal Calculate(int quantity, decimal unitPrice, string customerTier)
    {
        decimal base_ = quantity * unitPrice;
        if (customerTier == "GOLD") base_ *= 0.8m;
        if (quantity > 100) base_ -= base_ * 0.05m;
        if (unitPrice > 500m && customerTier != "GOLD") base_ += base_ * 0.02m;
        return Math.Round(base_, 2);
    }
}
```

You could spend an hour reverse-engineering the logic. Or you can just run it with a few inputs and capture the results:

```csharp
public class LegacyPricingEngineTests
{
    private readonly LegacyPricingEngine _engine = new();

    [Theory]
    [InlineData(10, 100m, "STANDARD", 1000.00)]
    [InlineData(10, 100m, "GOLD", 800.00)]
    [InlineData(150, 50m, "STANDARD", 7125.00)]
    [InlineData(150, 50m, "GOLD", 5700.00)]
    [InlineData(5, 600m, "STANDARD", 3060.00)]
    [InlineData(5, 600m, "GOLD", 2400.00)]
    public void Calculate_ReturnsExpectedOutput(
        int quantity, decimal unitPrice, string tier, decimal expected)
    {
        var result = _engine.Calculate(quantity, unitPrice, tier);
        Assert.Equal(expected, result);
    }
}
```

You generated those expected values by *running the code first* and recording what came out. The tests don't assert what the business logic should be — they assert what the current implementation produces. Now you can refactor `Calculate` with confidence: if anything changes, a test breaks.

## Snapshot Testing for Complex Output

The `[InlineData]` approach works well when the output is a scalar. For complex output — formatted strings, JSON, HTML, XML — snapshot testing is more practical. You capture the entire output as a file and compare future runs against it.

Here's a hand-rolled version using xUnit and file-based snapshots:

```csharp
public class InvoiceFormatterTests
{
    private readonly InvoiceFormatter _formatter = new();
    private const string SnapshotDir = "snapshots";

    private void VerifySnapshot(string testName, string actual)
    {
        var snapshotPath = Path.Combine(SnapshotDir, $"{testName}.txt");

        if (!File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(SnapshotDir);
            File.WriteAllText(snapshotPath, actual);
            return; // First run: approve and save
        }

        var expected = File.ReadAllText(snapshotPath);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FormatInvoice_StandardOrder_MatchesSnapshot()
    {
        var invoice = new Invoice(id: "INV-001", customerId: "C123", amount: 1500.00m);
        var result = _formatter.Format(invoice);
        VerifySnapshot(nameof(FormatInvoice_StandardOrder_MatchesSnapshot), result);
    }
}
```

On the first run, no snapshot exists, so the test writes the output to disk and passes. Commit that snapshot file alongside the test. On every subsequent run, the test loads the snapshot and compares — any change in output is caught immediately.

This is the golden master pattern: run once to establish the master, verify forever after.

## Using the Verify Library

Rolling your own snapshot infrastructure gets tedious. The **Verify** library (from Simon Cropp) is the most popular approval testing library in the .NET ecosystem and handles all of this for you, with better diff output and IDE integration.

Add it to your test project:

```bash
dotnet add package Verify.Xunit
```

Then your test becomes:

```csharp
using VerifyXunit;

[UsesVerify]
public class InvoiceFormatterTests
{
    private readonly InvoiceFormatter _formatter = new();

    [Fact]
    public Task FormatInvoice_StandardOrder_MatchesSnapshot()
    {
        var invoice = new Invoice(id: "INV-001", customerId: "C123", amount: 1500.00m);
        var result = _formatter.Format(invoice);
        return Verify(result);
    }
}
```

Note that the test returns `Task` — Verify's methods are async. On first run, the test fails and Verify writes two files to disk:

- `InvoiceFormatterTests.FormatInvoice_StandardOrder_MatchesSnapshot.received.txt` — what the code produced
- `InvoiceFormatterTests.FormatInvoice_StandardOrder_MatchesSnapshot.verified.txt` — the approved value (initially empty)

You review the `.received.txt` file, decide whether the output is what you expect, then rename (or copy) it to `.verified.txt`. That file gets committed to source control. On every subsequent run, Verify compares the received output against the verified file.

The workflow is explicit and intentional: you must *approve* the output before the test can pass.

## The Approval Workflow

The workflow for characterisation tests with Verify is worth spelling out clearly, because it's different from the usual red-green cycle:

1. **Write the test** — call the code under test and pass the result to `Verify(result)`.
2. **Run the test** — it fails on first run because there's no verified file yet.
3. **Review the received file** — check whether the output looks like what the code actually does (not what it should do).
4. **Approve it** — rename `.received.txt` to `.verified.txt`, or use a diff tool to accept the change.
5. **Commit both files** — the test code and the `.verified.txt` snapshot live in source control together.
6. **Refactor** — change the code. If behaviour changes unexpectedly, the test fails and Verify shows you a diff of what changed.
7. **Deliberately approve changes** — when you intentionally change behaviour, review and re-approve the new output.

Step 3 is where the "characterisation" part lives. You're not asserting what *should* happen — you're recording what *does* happen. If the output looks wrong, that's worth investigating (it might reveal a bug). But if you're capturing behaviour before a refactoring, you approve it as-is.

## Verifying Objects Directly

Verify also handles complex objects, not just strings. It serialises them to a human-readable text format:

```csharp
[Fact]
public Task ProcessOrder_ReturnsExpectedOrderState()
{
    var engine = new OrderProcessingEngine();
    var order = new Order(id: "ORD-42", customerId: "C789", items: new[]
    {
        new OrderItem("Widget", 3, 25.00m),
        new OrderItem("Gadget", 1, 149.99m)
    });

    var result = engine.Process(order);

    return Verify(result);
}
```

The verified file will contain a text representation of the `ProcessedOrder` object — all its properties serialised. If your refactoring accidentally changes how totals are calculated, or introduces a rounding difference, the test catches it.

## Scrubbing Non-Deterministic Values

Legacy code often produces output with timestamps, GUIDs, or other values that change on every run. Verify handles this with "scrubbers" — you tell it which parts of the output to ignore:

```csharp
[Fact]
public Task GenerateReport_ReturnsExpectedContent()
{
    var reporter = new LegacyReportGenerator();
    var report = reporter.Generate(customerId: "C123");

    return Verify(report)
        .ScrubMember<Report>(r => r.GeneratedAt)   // ignore timestamp
        .ScrubMember<Report>(r => r.ReportId);     // ignore generated GUID
}
```

You can also scrub by regex pattern if the non-deterministic data is embedded in a string:

```csharp
return Verify(htmlOutput)
    .ScrubLinesWithReplace(line =>
        System.Text.RegularExpressions.Regex.Replace(
            line, @"\d{4}-\d{2}-\d{2}", "DATE_SCRUBBED"));
```

The goal is to make the snapshot stable — the parts you care about should be deterministic, and the parts you can't control should be scrubbed.

## Combining with Unit Tests

Characterisation tests are a stepping stone, not a destination. The pattern I've found works well is:

1. **Add characterisation tests** before touching any legacy code. They act as a safety net.
2. **Start refactoring** — extract methods, introduce interfaces, name things properly.
3. **Replace each characterisation test** with targeted unit tests as you go. As code gets cleaner and its intent becomes obvious, you can write proper `[Fact]` tests that assert specific behaviours.
4. **Delete the characterisation tests** once they're covered by real unit tests.

The characterisation tests are scaffolding. Once the building is up, you remove the scaffolding. Keeping them around after you understand the code just adds maintenance overhead — every deliberate behaviour change requires re-approving snapshots that no longer need to exist.

That said, for output-heavy code (report formatters, document generators, anything that produces structured text), snapshot tests can be genuinely better long-term than exhaustive unit tests. A diff of a formatted invoice is more legible than fifty `Assert.Equal` calls. Use your judgement.

## Wrapping Up

Characterisation tests are one of the most practical tools you can reach for when working with legacy code. They let you start refactoring *now* rather than waiting until you've fully reverse-engineered the code, and they give you immediate feedback if you accidentally change something you didn't mean to.

The workflow is simple: run it, capture the output, approve it, commit it. Refactor. Let the tests catch accidental changes. Replace them with proper unit tests as you understand the code better.

The **Verify** library makes this almost frictionless in .NET. The diff output is readable, the approval workflow is explicit, and the integration with xUnit is seamless. If you're inheriting a legacy codebase — or if you have any code you're afraid to touch — start there. Write a characterisation test before you change a single line.

That wraps up the testing series. From unit tests to TDD to mocking to integration tests to property-based testing to characterisation tests — you've got the full toolkit now. Use whichever combination your situation calls for, and don't be afraid to mix approaches. The goal is confidence, not methodology purity.
