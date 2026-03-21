---
title: "Nullable Reference Types in C#"
date: 2026-03-21
tags: csharp, dotnet, nullability, language-features
image: nullable-reference-types-in-csharp.png
---

I have a complicated relationship with `null`.

We've been together since my early C# days. It's been mostly one-sided — `null` has crashed my apps, embarrassed me in production, and woken me up at 2am via PagerDuty alerts. I, in return, have typed `NullReferenceException` into Google more times than I'd like to admit. 🤦‍♂️

C# 8 introduced nullable reference types, and they've genuinely changed how I write code. Let me walk you through what they are, how to use them, and a few pitfalls to avoid.

## The Billion-Dollar Mistake

Tony Hoare — inventor of the null reference — famously called it his "billion-dollar mistake." Harsh, but probably underselling it.

In C#, every reference type has historically been implicitly nullable. A `string` could be `null`. A `List<T>` could be `null`. Your custom `User` class could absolutely be `null`, and the compiler didn't care one bit.

```csharp
string name = null; // Totally fine. No warnings. Nothing.
Console.WriteLine(name.Length); // Boom. NullReferenceException at runtime.
```

The compiler shrugged. The runtime exploded. You cried.

## Enabling Nullable Reference Types

From C# 8 onwards, you can opt in to nullable annotations. In modern .NET projects, it's typically enabled by default in the `.csproj`:

```xml
<Nullable>enable</Nullable>
```

You can also toggle it per-file with directives if you're migrating a large codebase gradually:

```csharp
#nullable enable
// nullable analysis active here

#nullable disable
// back to the wild west
```

I'd strongly recommend enabling it project-wide. Yes, you'll get a wave of warnings when you first turn it on. That's the point — those warnings represent real bugs waiting to happen.

## The ? Annotation

Once enabled, the compiler treats reference types as non-nullable by default. If you *want* to allow null, you have to say so explicitly with `?`:

```csharp
string name = "Mark";        // Non-nullable: can't be null (compiler will warn)
string? nickname = null;     // Nullable: allowed to be null

void Greet(string name)      // Caller must pass a real string
{
    Console.WriteLine($"Hello, {name}!");
}

void GreetOptional(string? name)  // null is valid here
{
    Console.WriteLine($"Hello, {name ?? "stranger"}!");
}
```

This is simple, but it changes everything. Your method signatures now communicate intent. `string?` says "this might be null, handle it." `string` says "I promise this isn't null."

## Warnings, Not Errors

Here's the thing: nullable reference types are a *warning* system, not an error system. The compiler warns you, but it still compiles.

```csharp
#nullable enable

string? message = GetMessage(); // might be null

Console.WriteLine(message.Length); // ⚠️ Warning: Dereference of a possibly null reference
```

That warning won't stop your build. Which is both a feature and a footgun.

> Some teams crank `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in their CI pipeline and then wonder why their pull requests are full of `!` suppressions. There's a balance to strike. Enable the warnings, fix the real ones, and don't paper over legitimate issues with suppressions. A warning you ignore isn't better than no warning at all.

The pragmatic move: fix the genuine nullability issues as you go. Use suppressions sparingly, and only when you *know* something can't be null at that point in the code.

## The Null-Forgiving Operator !

The `!` operator is the escape hatch. It tells the compiler "trust me, this isn't null":

```csharp
string? value = GetValueFromSomewhere();

// I know it's not null here because of business logic the compiler can't see
Console.WriteLine(value!.Length);
```

It silences the warning. It doesn't add a null check. If you're wrong about `value` being non-null, you still get a `NullReferenceException` at runtime.

Use `!` when:
- You genuinely have context the compiler doesn't (e.g., checked null two lines above)
- You're working with legacy code or external APIs that predate nullable annotations

Don't use `!` to:
- Silence warnings you don't feel like fixing
- "Clean up" your code before a code review
- Pretend the problem doesn't exist

I'll be honest — I've done all three of those at some point. 🤦‍♂️ Don't be past-me.

## Null-Conditional and Null-Coalescing Operators

These existed before nullable reference types, but they pair beautifully with them.

**Null-conditional `?.`** — only call the member if not null:

```csharp
string? name = GetName();
int? length = name?.Length; // null if name is null, otherwise the length
```

**Null-coalescing `??`** — provide a fallback value:

```csharp
string? name = GetName();
string display = name ?? "Anonymous";
```

**Null-coalescing assignment `??=`** — assign if null:

```csharp
string? cachedValue = null;
cachedValue ??= ComputeExpensiveValue(); // only computes if still null
```

Chaining them together is where things get elegant:

```csharp
string display = user?.Profile?.DisplayName ?? user?.Username ?? "Unknown";
```

That would've been a four-line if-block in older C#. I think it's genuinely nicer this way, though I accept not everyone agrees.

## Patterns: Constructor Init, Required Properties, Default Values

The real power of nullable reference types emerges when you think about how you model your data.

**Initialise everything in the constructor:**

```csharp
public class UserProfile
{
    public string Username { get; }
    public string Email { get; }
    public string? DisplayName { get; set; } // optional, can be null

    public UserProfile(string username, string email)
    {
        Username = username;
        Email = email;
    }
}
```

The compiler sees `Username` and `Email` assigned in the constructor and is happy. `DisplayName` is explicitly nullable.

**Required properties (C# 11+):**

```csharp
public class UserProfile
{
    public required string Username { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
}

// Usage — compiler enforces Username and Email are provided
var profile = new UserProfile
{
    Username = "mclearwater",
    Email = "mark@example.com"
};
```

`required` is one of those features that makes me wish C# had added it a decade earlier.

**Default values where sensible:**

```csharp
public class SearchOptions
{
    public string Query { get; set; } = string.Empty;
    public int PageSize { get; set; } = 20;
    public string? Category { get; set; } // optional filter
}
```

Explicitly defaulting to `string.Empty` rather than `null` means you never have to check for null on `Query`. It's always a valid (if empty) string.

## Real-World Example: An API Response Model

Let's tie it together with something you'd actually encounter. You're consuming an external API that returns user data — some fields always present, some optional:

```csharp
#nullable enable

public class ApiUser
{
    // Always present in the response
    public required string Id { get; init; }
    public required string Email { get; init; }

    // Optional — might not be set
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? AvatarUrl { get; init; }

    // Computed property — safe because we handle null
    public string DisplayName =>
        (FirstName != null && LastName != null)
            ? $"{FirstName} {LastName}"
            : FirstName ?? LastName ?? Email;
}

public class UserService
{
    public string GetGreeting(ApiUser? user)
    {
        if (user is null)
            return "Hello, guest!";

        return $"Welcome back, {user.DisplayName}!";
    }

    public string GetAvatarUrl(ApiUser user)
    {
        // AvatarUrl is nullable, so provide a fallback
        return user.AvatarUrl ?? "/images/default-avatar.png";
    }
}
```

Notice what the method signatures tell you now. `GetGreeting(ApiUser? user)` accepts null — the caller knows they need to handle that case (or not, and pass null deliberately). `GetAvatarUrl(ApiUser user)` demands a real user — the caller is responsible for ensuring it's not null before calling.

This is the real payoff. Your API communicates contracts through types.

## Is It Worth the Noise?

When you first enable nullable reference types on an existing project, you'll get a lot of warnings. It can feel overwhelming.

In my experience: yes, it's worth it. Most of those warnings are real issues. Some you'll fix by initialising properly. Some will make you realise you've been relying on null-as-sentinel-value in ways that cause subtle bugs. A few will be things the compiler just can't prove but you know are fine — and for those, `!` is there.

The goal isn't zero warnings via `!` spam. The goal is code where null is intentional, documented in the types, and handled explicitly.

Maybe not glamorous. Maybe not the flashiest C# feature. But it's quietly one of the most valuable things added to the language in recent years.

Well, in my opinion anyway. Your mileage may vary.
