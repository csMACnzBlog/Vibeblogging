---
title: Guard Clauses in C# for Cleaner Methods
date: 2026-05-22
tags: csharp, dotnet, clean-code, architecture
image: guard-clauses-in-csharp-for-cleaner-methods.png
---

Nested `if` blocks make methods harder to read than they need to be. By the time you reach the main logic, you're scrolling past layers of defensive checks.

Guard clauses flatten that structure and make intent obvious.

## The Before

```csharp
public decimal CalculateDiscount(Customer? customer, decimal total)
{
    if (customer != null)
    {
        if (total > 0)
        {
            if (customer.IsActive)
            {
                if (customer.Tier == "gold")
                {
                    return total * 0.15m;
                }

                return total * 0.05m;
            }
        }
    }

    return 0m;
}
```

Technically correct, but not fun to reason about.

## The After

```csharp
public decimal CalculateDiscount(Customer customer, decimal total)
{
    ArgumentNullException.ThrowIfNull(customer);

    if (total <= 0) return 0m;
    if (!customer.IsActive) return 0m;

    return customer.Tier switch
    {
        "gold" => total * 0.15m,
        "silver" => total * 0.10m,
        _ => total * 0.05m
    };
}
```

Now preconditions are handled up front and the business rule is easy to scan.

## Use Built-In Guard Helpers

Modern .NET gives you good defaults:

```csharp
ArgumentNullException.ThrowIfNull(order);
ArgumentException.ThrowIfNullOrWhiteSpace(order.CustomerId);
ArgumentOutOfRangeException.ThrowIfNegative(quantity);
```

These are concise and keep exception types consistent.

## Guard at the Right Boundary

I treat guards as boundary protection:

- Public methods validate incoming arguments
- Application handlers validate command/query shape
- Domain rules use domain-specific exceptions/results

That split avoids duplicate validation layers while still keeping failures explicit.

## Wrapping Up

Guard clauses are a small habit with a big readability payoff. Methods stay flatter, intent gets clearer, and invalid inputs fail early.

If you've got deeply nested service methods, this is an easy cleanup to start with.
