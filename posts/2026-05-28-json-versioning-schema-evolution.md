---
title: JSON Versioning & Schema Evolution
date: 2026-05-28
tags: csharp, dotnet, serialization, api-design
image: json-versioning-schema-evolution.png
---

If you've ever shipped an API and then needed to rename a property two weeks later, you already know the pain: clients don't upgrade all at once.

That means your JSON contract has to evolve without breaking old consumers. The good news is you don't need a giant rewrite to do it. A few deliberate patterns in `System.Text.Json` go a long way.

Let's walk through practical options I keep reaching for.

## Start with additive changes

The safest move is adding new properties while keeping old ones for at least one transition window.

```csharp
using System.Text.Json.Serialization;

public sealed class CustomerResponse
{
    // Existing contract (v1)
    public string FullName { get; init; } = string.Empty;

    // New contract (v2)
    public string? GivenName { get; init; }
    public string? FamilyName { get; init; }
}
```

If old clients only read `FullName`, they still work. New clients can opt into the richer shape when they're ready.

## Handle renamed input with `[JsonExtensionData]`

Sometimes you need to accept both old and new property names during a migration.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class UpdateProfileRequest
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    public string? ResolveLegacyDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(DisplayName))
        {
            return DisplayName;
        }

        if (Extra is not null && Extra.TryGetValue("name", out var legacyName))
        {
            return legacyName.GetString();
        }

        return null;
    }
}
```

This lets you accept `displayName` (new) and `name` (legacy) without fragile manual JSON parsing.

## Write both shapes with a custom converter

When you must output old or new JSON shape based on API version, a converter keeps that logic in one place.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public enum ContractVersion { V1, V2 }

public sealed record Money(decimal Amount, string Currency);

public sealed class MoneyConverter(ContractVersion version) : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        decimal amount = root.GetProperty("amount").GetDecimal();
        string currency = root.TryGetProperty("currencyCode", out var code)
            ? code.GetString() ?? "USD"
            : root.GetProperty("currency").GetString() ?? "USD";

        return new Money(amount, currency);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);

        if (version is ContractVersion.V2)
        {
            writer.WriteString("currencyCode", value.Currency);
        }
        else
        {
            writer.WriteString("currency", value.Currency);
        }

        writer.WriteEndObject();
    }
}

var v1Options = new JsonSerializerOptions();
v1Options.Converters.Add(new MoneyConverter(ContractVersion.V1));

var v2Options = new JsonSerializerOptions();
v2Options.Converters.Add(new MoneyConverter(ContractVersion.V2));
```

Now version-specific behavior is explicit and testable.

## Deprecate with telemetry, not guesswork

I used to remove legacy fields based on calendar dates alone. That's risky.

A better approach is:

1. mark legacy fields as deprecated in docs/OpenAPI
2. log when legacy fields are read
3. remove them only after usage is near zero

If you can measure usage, you can remove old contracts confidently instead of hoping nobody notices.

## A small migration checklist

When changing JSON contracts, I keep this checklist nearby:

- prefer additive changes first
- accept old + new input names during transition
- centralize serialization branching in converters
- version endpoints only when shape differences are substantial
- remove legacy properties only after telemetry proves they're unused

Schema evolution is less about clever code and more about boring consistency. That's a good thing.

Your API can move fast *and* stay stable — as long as you treat contracts as long-lived agreements, not temporary implementation details.
