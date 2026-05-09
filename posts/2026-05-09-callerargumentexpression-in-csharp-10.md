---
title: CallerArgumentExpression in C# 10
date: 2026-05-09
tags: csharp, dotnet, tutorial
image: callerargumentexpression-in-csharp-10.png
---

If you've ever written guard clauses and felt annoyed repeating parameter names as strings, `CallerArgumentExpression` fixes that. It's one of those small C# 10 features that quietly makes utility APIs feel much better.

## The Problem with Manual Parameter Names

Before C# 10, argument validation helpers usually looked like this:

```csharp
public static class Guard
{
    public static void AgainstNull<T>(T? value, string paramName) where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }
}

string? email = null;
Guard.AgainstNull(email, nameof(email));
```

It works, but it's repetitive. And if someone passes the wrong `nameof(...)` (or a raw string), your exception message lies.

## Enter `CallerArgumentExpression`

`CallerArgumentExpression` lets your method capture the original expression text used at the call site.

```csharp
using System.Runtime.CompilerServices;

public static class Guard
{
    public static void AgainstNull<T>(
        T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }
}

string? email = null;
Guard.AgainstNull(email); // throws with paramName = "email"
```

Now the caller passes only the value, and the compiler provides the expression text for you.

## Why This Is Better in Real Projects

You get a few practical wins immediately:

- Less boilerplate at call sites
- Better consistency in exception messages
- Fewer chances to mismatch `value` and `paramName`

It also scales nicely when your guard library grows to include null/empty/range checks.

## Capturing Complex Expressions

The captured string isn't just variable names. It's the full expression text.

```csharp
string? firstName = "Ada";
string? lastName = null;

Guard.AgainstNull(firstName + " " + lastName);
// If the result is null, paramName becomes: firstName + " " + lastName
```

That can be great for debugging because you see exactly what the caller passed.

## A More Useful Guard Example

Here's a guard that validates strings and includes the caller expression automatically:

```csharp
using System.Runtime.CompilerServices;

public static class Guard
{
    public static string AgainstNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        return value;
    }
}

var username = Guard.AgainstNullOrWhiteSpace(input.UserName);
```

No `nameof(input.UserName)` needed, and your exception still points to `input.UserName`.

## Pairing with Custom Validation APIs

`CallerArgumentExpression` is especially handy for fluent validation-style helpers:

```csharp
using System.Runtime.CompilerServices;

public static class Ensure
{
    public static int Positive(
        int value,
        [CallerArgumentExpression(nameof(value))] string? expression = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(expression, "Value must be positive.");
        }

        return value;
    }
}

int retryCount = Ensure.Positive(configuration.MaxRetries);
```

This keeps your call sites clean while still giving very specific failure messages.

## A Couple of Gotchas

A few things are worth remembering:

1. It only works when the target parameter has a default value.
2. It's compile-time metadata; reflection or dynamic invocation can behave differently.
3. The captured text is expression text, not evaluated values.

So it's perfect for diagnostics and exception parameter names, but not for logging sensitive values.

## Wrapping Up

`CallerArgumentExpression` is small, but it improves day-to-day C# code quality:

- Cleaner guard/validation calls
- More reliable exception parameter names
- Better debugging context from real call-site expressions

If you maintain any helper APIs that currently require `nameof(...)` everywhere, this feature is an easy upgrade that pays off quickly.
