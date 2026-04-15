---
title: Authentication and Authorization in ASP.NET Core
date: 2026-04-15
tags: aspnetcore, dotnet, security, authentication, authorization
image: authentication-and-authorization-in-aspnetcore.png
---

If you're building any real ASP.NET Core app, you'll hit this question quickly: "How do I lock this down without making everything painful?"

Authentication and authorization sound similar, but they solve different problems:

- **Authentication** asks: "Who are you?"
- **Authorization** asks: "What are you allowed to do?"

Let's walk through a practical setup you can drop into a new API.

## Start with Authentication

For APIs, JWT bearer tokens are usually the easiest option. Add this to `Program.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
```

The middleware order matters. `UseAuthentication()` must run before `UseAuthorization()`, or policy checks won't have an authenticated user to evaluate.

## Issue a Token from a Login Endpoint

A minimal token endpoint can look like this:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("token")]
    public IActionResult CreateToken(LoginRequest request)
    {
        if (request.Username != "demo" || request.Password != "demo123")
        {
            return Unauthorized();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, request.Username),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("scope", "orders.read")
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return Ok(new
        {
            access_token = new JwtSecurityTokenHandler().WriteToken(token)
        });
    }
}

public record LoginRequest(string Username, string Password);
```

In production, replace the hardcoded credential check with your identity store.

## Add Authorization Rules

Now let's protect endpoints. You can use plain `[Authorize]`, role-based checks, or policy-based checks.

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "CanReadOrders")]
    public IActionResult GetOrders()
    {
        return Ok(new[] { "Order-1001", "Order-1002" });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public IActionResult DeleteOrder(string id)
    {
        return NoContent();
    }
}
```

And register the policy:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanReadOrders", policy =>
        policy.RequireClaim("scope", "orders.read"));
});
```

This gives you a clean split:

- Roles for coarse permissions (like admin-only actions)
- Policies for business-specific rules (like scopes/claims)

## Common Mistakes (and quick fixes)

### "Everything returns 401"

Usually your token validation settings don't match the token you're issuing (`issuer`, `audience`, or key mismatch).

### "I get 403 instead of 401"

That's expected when authentication succeeded but authorization failed. 401 means "not logged in." 403 means "logged in, but not allowed."

### Missing `UseAuthentication()`

If you only call `UseAuthorization()`, `[Authorize]` always fails because no identity is created.

## Final Thoughts

A good ASP.NET Core security setup doesn't need to be complicated:

1. Configure one clear authentication scheme
2. Keep token claims intentional and minimal
3. Use policies for domain rules instead of scattering claim checks everywhere

Start simple, then add more advanced flows (refresh tokens, external identity providers, MFA) when your app actually needs them.
