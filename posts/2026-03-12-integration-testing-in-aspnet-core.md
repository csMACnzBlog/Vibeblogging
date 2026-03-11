---
title: Integration Testing in ASP.NET Core
date: 2026-03-12
tags: dotnet, csharp, testing, aspnetcore
image: integration-testing-in-aspnet-core.png
---

We've been building up a solid testing toolkit this week. We covered [unit testing with xUnit](unit-testing-in-csharp-with-xunit.html), [TDD](test-driven-development-in-csharp.html), and [mocking with Moq](mocking-with-moq-in-csharp.html). Unit tests are great, but they only tell you that your individual pieces work in isolation. Integration tests tell you whether those pieces actually work *together* — and that's where things get interesting.

## What Integration Tests Actually Are

Unit tests mock everything. That's their superpower and their limitation. When you mock your database and your HTTP clients and your file system, you're testing your logic in a vacuum. That's useful! But it doesn't catch the bug where your Entity Framework query returns results in a different order than your mock did, or where your middleware is eating an error before it reaches your handler.

**Integration tests run more of the real stack.** They spin up your actual application — or something very close to it — and test it through its real interfaces. For an ASP.NET Core app, that usually means making actual HTTP requests and checking real HTTP responses.

The tradeoff is speed. Integration tests are slower than unit tests. They also tend to be more brittle. I don't think that means you should skip them — I think it means you should write fewer of them and make them count.

## WebApplicationFactory

ASP.NET Core ships with a package specifically for integration testing: `Microsoft.AspNetCore.Mvc.Testing`. The star of this package is `WebApplicationFactory<TProgram>`.

It starts up your application in-process, without needing a real HTTP server. You get a fully configured `HttpClient` that routes requests directly to your app. It's fast (for an integration test), it's isolated, and it uses your real `Program.cs` configuration.

First, add the package to your test project:

```bash
dotnet add package Microsoft.AspNetCore.Mvc.Testing
```

Your test project also needs a reference to your web project. In your `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\MyApi\MyApi.csproj" />
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
</ItemGroup>
```

One more thing: `WebApplicationFactory` needs access to your app's internals. In your web project, make sure `Program` is a public partial class so the test assembly can reference it. Add this at the bottom of `Program.cs` (or in a separate file like `ProgramExtensions.cs`):

```csharp
// Program.cs (bottom) — exposes Program as a type parameter for WebApplicationFactory
public partial class Program { }
```

This makes `Program` accessible as a type parameter.

## Your First Integration Test

Here's a minimal test that spins up the app and hits an endpoint:

```csharp
public class WeatherForecastTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WeatherForecastTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetWeatherForecast_ReturnsSuccessStatusCode()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/weatherforecast");

        response.EnsureSuccessStatusCode();
    }
}
```

`IClassFixture<WebApplicationFactory<Program>>` tells xUnit to create the factory once per test class and inject it into the constructor. That's important — spinning up the app is expensive, so you want to share the instance across tests.

`_factory.CreateClient()` gives you an `HttpClient` pre-configured to talk to your in-process server. No port numbers, no localhost URLs, just make requests.

## Customising the Factory

The real power comes from overriding parts of your app in tests. You probably don't want integration tests hitting your production database. You want an in-memory database, or a test database, or a carefully controlled fake.

```csharp
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real database context
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));

            if (descriptor != null)
                services.Remove(descriptor);

            // Add an in-memory database
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestDb");
            });

            // Build the service provider and seed the database
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            SeedTestData(db);
        });
    }

    private static void SeedTestData(AppDbContext db)
    {
        db.Products.AddRange(
            new Product { Id = 1, Name = "Widget", Price = 9.99m },
            new Product { Id = 2, Name = "Gadget", Price = 24.99m }
        );
        db.SaveChanges();
    }
}
```

Now use it in your tests:

```csharp
public class ProductsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProductsTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProducts_ReturnsAllProducts()
    {
        var response = await _client.GetAsync("/api/products");
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<List<Product>>();
        Assert.Equal(2, products!.Count);
    }
}
```

You've swapped the database without touching your app code. That's the pattern.

## Replacing Services with Fakes

Sometimes you need to replace more than just the database — maybe you have an email service or a payment processor you definitely don't want running in tests.

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        // Replace the real email service with a fake
        services.AddScoped<IEmailService, FakeEmailService>();
    });
}
```

If a registration already exists for `IEmailService`, adding another one will use the last registration (for scoped/transient). For singletons it's slightly different — you may need to remove the original first. I've been bitten by this more than once, so always verify which registration wins in your specific case.

## Test Isolation

Here's something that'll save you headaches: **integration tests that share state will eventually fail in mysterious ways.** If test A seeds the database and test B expects an empty database, the order of execution suddenly matters. That's a bad time.

A few strategies:

**Option 1: Reset the database between tests.** Implement `IAsyncLifetime` on your test class to clean up after each test.

```csharp
public class ProductsTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProductsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Reset the in-memory database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Products.RemoveRange(db.Products);
        await db.SaveChangesAsync();
    }
}
```

**Option 2: Create a fresh factory per test.** Slower, but simpler. Use `IDisposable` instead of `IClassFixture`.

**Option 3: Design tests to not depend on shared state.** Each test seeds its own data and queries only what it seeded. This is my preference when feasible — it makes tests self-describing.

## Authenticating in Integration Tests

If your endpoints require authentication, you have a couple of options.

The simplest is to configure your test app to allow anonymous access everywhere:

```csharp
builder.ConfigureServices(services =>
{
    services.AddSingleton<IAuthorizationHandler, AllowAnonymousAuthorizationHandler>();
});
```

The better approach for most cases is to create fake authentication. ASP.NET Core has a `TestAuthHandler` pattern for this:

```csharp
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

Register it in your factory and configure the client to send the right header. It's a bit of ceremony, but once you've done it once it becomes boilerplate you copy between projects.

## When to Use Integration Tests vs Unit Tests

My rule of thumb: **write unit tests for logic, integration tests for wiring.**

If you're testing an algorithm, a business rule, or a calculation — unit test it. Mock the dependencies, run it fast, test every edge case.

If you're testing that your controller actually calls the right service, that your middleware runs in the right order, that your Entity Framework query produces the right SQL, that your endpoint returns the right status code for the right input — integration test it.

I tend to have many more unit tests than integration tests. Unit tests catch bugs faster and give clearer failure messages. But integration tests catch the category of bug where all the units work individually and the assembled thing still doesn't work. Those bugs are expensive, and a handful of well-targeted integration tests are worth writing.

One thing I'd push back on: don't write integration tests for things that are already well-covered by unit tests just to feel more confident. That's coverage theatre. Write integration tests for the things that genuinely need integration to be tested.

## What's Next

You've now got a complete testing toolkit: unit tests for logic, mocks for isolation, TDD for design feedback, and integration tests for the full stack. The next question is how to actually run all of this in CI — but that's a story for another day.

The `WebApplicationFactory` approach is one of those things that looks complex at first glance but is actually quite elegant once you've used it a few times. The fact that you can test your entire HTTP stack in-process, swapping out dependencies cleanly, is genuinely useful. Use it on the seams of your application — the HTTP endpoints, the database queries, the places where components meet — and you'll catch the bugs that matter.
