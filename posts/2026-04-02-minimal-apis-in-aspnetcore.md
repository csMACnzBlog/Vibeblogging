---
title: Minimal APIs in ASP.NET Core
date: 2026-04-02
tags: aspnetcore, dotnet, csharp, tutorial
image: minimal-apis-in-aspnetcore.png
---

When ASP.NET Core Minimal APIs landed in .NET 6, I'll be honest — I wasn't sure what to make of them. Controllers had worked fine for years. Why change things? Then I used them for a small internal tool and never looked back.

Minimal APIs strip away the ceremony of controllers, attributes, and action methods and let you write HTTP endpoints that feel almost like writing plain C# functions. They're not a replacement for everything controllers do, but for a lot of common scenarios they're a much better fit.

## The Simplest Thing That Works

Here's a complete ASP.NET Core app using Minimal APIs:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello, world!");

app.Run();
```

That's it. No controllers, no startup class split across two files, no `[HttpGet]` attribute to forget. You call `MapGet`, pass a route, and return something. Done.

The return value gets serialised for you. Return a string and it comes back as `text/plain`. Return an object and you get JSON:

```csharp
app.MapGet("/users/{id}", (int id) => new { Id = id, Name = "Alice" });
```

ASP.NET Core figures out the binding: `id` comes from the route because there's a `{id}` token. It's the same model binders you already know, just without the attribute boilerplate.

## Organising Routes Beyond the Basics

One concern people raise — and it's fair — is that jamming every endpoint into `Program.cs` gets messy fast. The answer is route groups and extension methods.

Route groups let you share a common prefix and middleware:

```csharp
var users = app.MapGroup("/users");

users.MapGet("/", GetAllUsers);
users.MapGet("/{id}", GetUserById);
users.MapPost("/", CreateUser);
users.MapPut("/{id}", UpdateUser);
users.MapDelete("/{id}", DeleteUser);
```

And you can move those endpoints into their own static class so `Program.cs` stays clean:

```csharp
// Program.cs
app.MapGroup("/users").MapUserEndpoints();

// UserEndpoints.cs
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        return group;
    }

    private static IResult GetAll(IUserRepository repo)
        => Results.Ok(repo.GetAll());

    private static IResult GetById(int id, IUserRepository repo)
    {
        var user = repo.GetById(id);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static async Task<IResult> Create(
        CreateUserRequest request,
        IUserRepository repo,
        CancellationToken ct)
    {
        var user = await repo.CreateAsync(request, ct);
        return Results.Created($"/users/{user.Id}", user);
    }
}
```

This is the pattern I actually reach for. Each feature area lives in its own class. `Program.cs` just wires them up.

## Dependency Injection Just Works

You might be wondering how `IUserRepository` ends up in those handler methods. The answer is: the same way it always has. ASP.NET Core resolves parameters from the DI container automatically.

```csharp
builder.Services.AddScoped<IUserRepository, SqlUserRepository>();
```

Declare the service, and any handler that asks for it gets it. No `[FromServices]` attribute required — ASP.NET Core inspects the type and decides where to source each parameter from:

- Route tokens → from the URL
- Query string types → from the query string  
- Known services → from DI
- Complex types → deserialized from the request body

It's surprisingly smart. The edge cases are well-documented and the defaults cover most scenarios.

## Validation and Problem Details

Controllers had `ModelState`. Minimal APIs don't have that built-in, which is actually fine — it pushes you toward explicit validation that's easier to test.

A clean approach is to use the `Results` class to return structured errors:

```csharp
app.MapPost("/users", async (CreateUserRequest request, IUserRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["Name is required"]
        });
    }

    var user = await repo.CreateAsync(request);
    return Results.Created($"/users/{user.Id}", user);
});
```

`Results.ValidationProblem` returns a RFC 7807 Problem Details response with status 422. The same format that `[ApiController]` generated automatically — just explicit now.

If you want automatic validation, you can wire up a library like FluentValidation with a filter:

```csharp
app.MapPost("/users", CreateUser)
   .AddEndpointFilter<ValidationFilter<CreateUserRequest>>();
```

The filter approach is composable. You can stack multiple filters and reason about them independently.

## Returning the Right Status Codes

The `Results` and `TypedResults` classes cover pretty much everything you need:

```csharp
// 200 OK with body
Results.Ok(data)

// 201 Created with Location header
Results.Created("/users/42", user)

// 204 No Content
Results.NoContent()

// 400 with problem details
Results.Problem("Something went wrong", statusCode: 400)

// 404
Results.NotFound()

// 401 / 403
Results.Unauthorized()
Results.Forbid()

// Stream a file
Results.File(stream, "application/pdf")
```

`TypedResults` is the same but statically typed, which makes handler return types easier to declare and makes OpenAPI generation more accurate:

```csharp
private static Results<Ok<User>, NotFound> GetById(int id, IUserRepository repo)
{
    var user = repo.GetById(id);
    return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
}
```

The union return type tells the framework — and any OpenAPI tooling — exactly what responses to expect.

## Authentication and Authorization

Securing endpoints is a one-liner:

```csharp
app.MapGet("/admin", GetAdminData).RequireAuthorization("AdminPolicy");
app.MapGet("/public", GetPublicData).AllowAnonymous();
```

Apply policies per-endpoint, or apply them to a whole group:

```csharp
var admin = app.MapGroup("/admin").RequireAuthorization("AdminPolicy");
admin.MapGet("/users", ListUsers);
admin.MapGet("/settings", GetSettings);
```

The same authorization infrastructure you use with controllers — roles, policies, claims — works exactly the same way here.

## OpenAPI / Swagger

In .NET 9, Microsoft shipped its own OpenAPI document generation that works out of the box with Minimal APIs. Add a package, call a method:

```csharp
builder.Services.AddOpenApi();

// ...

app.MapOpenApi();
```

Then annotate endpoints to improve the generated docs:

```csharp
app.MapPost("/users", CreateUser)
   .WithName("CreateUser")
   .WithSummary("Create a new user")
   .WithDescription("Creates a user and returns the created resource.")
   .Produces<User>(201)
   .ProducesProblem(400)
   .ProducesProblem(422);
```

It's more verbose than Swagger annotations were, but it's explicit and sits right next to the endpoint definition.

## When to Reach for Minimal APIs

I use Minimal APIs for:

- **New projects** where I'm not carrying controller baggage
- **Microservices** with a small focused surface area
- **Background workers that expose a few management endpoints**
- **Prototyping** — the low ceremony speeds up iteration

I still think about controllers for:

- **Large teams** where the controller convention provides guardrails
- **Legacy codebases** where mixing styles adds cognitive overhead
- **Complex APIs** where action filters and model binding customisation are already in place

There's no rule that says you can't mix both in the same app, but I've found it's usually better to pick one and stick with it per-project.

## Wrapping Up

Minimal APIs remove a lot of ceremony without removing the things that matter: DI, middleware, authentication, validation, and OpenAPI all work the same way. What you lose is the opinion that everything must live inside a controller class, and that turns out to be surprisingly liberating.

If you've been putting off trying them because you thought they were only for toy examples — give them a proper go. Route groups and the extension method pattern scale surprisingly well.
