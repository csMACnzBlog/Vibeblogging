---
title: Validation with FluentValidation
date: 2026-04-08
tags: aspnetcore, dotnet, csharp, tutorial
image: validation-with-fluentvalidation.png
---

If you've been using data annotations to validate request models — `[Required]`, `[MaxLength]`, `[Range]` — you've probably hit the wall where they stop being enough. Conditional rules, cross-property validation, custom error messages that don't read like error codes. Data annotations work until they don't, and then they get messy fast.

FluentValidation takes a different approach: validators are plain C# classes with a fluent API. Rules live with the validator, not scattered across your model as attributes. It's more code upfront, but it scales cleanly as requirements grow.

## Getting Started

Add the package:

```bash
dotnet add package FluentValidation.AspNetCore
```

A validator inherits from `AbstractValidator<T>` and defines rules in its constructor:

```csharp
using FluentValidation;

public class CreateOrderRequest
{
    public string ProductCode { get; set; } = "";
    public int Quantity { get; set; }
    public string ShippingAddress { get; set; } = "";
}

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .LessThanOrEqualTo(100);

        RuleFor(x => x.ShippingAddress)
            .NotEmpty()
            .MaximumLength(200);
    }
}
```

Rules are applied left to right. `NotEmpty` runs first, then `MaximumLength` — and by default, if `NotEmpty` fails, the chain stops there rather than piling on redundant error messages. That's the default behaviour for each rule chain; you can change it with `.Cascade(CascadeMode.Continue)` if you want all errors at once.

## Wiring It Into ASP.NET Core

Register validators from an assembly and swap out the default model validation:

```csharp
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Register all validators in the current assembly
builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>();
```

If you're using controllers with `[ApiController]`, you can opt into automatic model state validation so FluentValidation runs before your action method is called. This requires adding `AddFluentValidationAutoValidation()` alongside your validator registrations:

```csharp
// MVC/controller-based integration — wires validators into ModelState automatically
builder.Services.AddFluentValidationAutoValidation();
```

That approach is still fully supported and works well for controller-based APIs. It's convenient because you don't need to call the validator manually in each action — ModelState returns 400 errors automatically when validation fails. That said, it is MVC-specific and doesn't work with Minimal APIs. For more control, or if you're mixing both styles, I prefer injecting and calling validators explicitly.

## Explicit Validation

Inject `IValidator<T>` wherever you need it:

```csharp
[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly IValidator<CreateOrderRequest> _validator;
    private readonly IOrderService _orderService;

    public OrdersController(
        IValidator<CreateOrderRequest> validator,
        IOrderService orderService)
    {
        _validator = validator;
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request)
    {
        var result = await _validator.ValidateAsync(request);

        if (!result.IsValid)
        {
            return ValidationProblem(result.ToDictionary());
        }

        var orderId = await _orderService.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = orderId }, null);
    }
}
```

`result.Errors` contains a list of `ValidationFailure` objects with the property name, error message, and attempted value. `ToDictionary()` converts them to the shape `ValidationProblem` expects — a dictionary of field names to error message arrays. That matches the standard RFC 7807 problem details format, so API clients get a consistent structure.

## Minimal APIs

For Minimal APIs, wire it up the same way:

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request,
    IValidator<CreateOrderRequest> validator,
    IOrderService orderService) =>
{
    var result = await validator.ValidateAsync(request);

    if (!result.IsValid)
    {
        return Results.ValidationProblem(result.ToDictionary());
    }

    var orderId = await orderService.CreateAsync(request);
    return Results.Created($"/orders/{orderId}", null);
});
```

Same pattern, minimal ceremony. If you find yourself repeating the validate-and-return block everywhere, that's a good candidate for a filter or extension method.

## Custom Error Messages

The default messages are readable ("'Quantity' must be greater than '0'"), but you'll often want something more domain-specific. Chain `.WithMessage()` on any rule:

```csharp
RuleFor(x => x.Quantity)
    .GreaterThan(0)
    .WithMessage("Quantity must be at least 1.")
    .LessThanOrEqualTo(100)
    .WithMessage("Quantity cannot exceed 100 per order.");
```

You can reference the submitted value in the message with `{PropertyValue}`, or use other built-in placeholders like `{PropertyName}`, `{MinLength}`, `{MaxLength}`:

```csharp
RuleFor(x => x.ProductCode)
    .NotEmpty()
    .WithMessage("Product code is required.")
    .MaximumLength(20)
    .WithMessage("Product code '{PropertyValue}' exceeds the maximum length of {MaxLength} characters.");
```

## Conditional Rules

This is where FluentValidation pulls ahead of data annotations. Rules can be conditional based on other properties:

```csharp
public class ShipmentRequest
{
    public string DeliveryType { get; set; } = "";
    public string? PoBoxNumber { get; set; }
    public string? StreetAddress { get; set; }
}

public class ShipmentRequestValidator : AbstractValidator<ShipmentRequest>
{
    public ShipmentRequestValidator()
    {
        RuleFor(x => x.DeliveryType)
            .NotEmpty()
            .Must(x => x is "standard" or "express" or "pobox")
            .WithMessage("Delivery type must be 'standard', 'express', or 'pobox'.");

        RuleFor(x => x.PoBoxNumber)
            .NotEmpty()
            .WithMessage("PO Box number is required for PO Box delivery.")
            .When(x => x.DeliveryType == "pobox");

        RuleFor(x => x.StreetAddress)
            .NotEmpty()
            .WithMessage("Street address is required for standard and express delivery.")
            .When(x => x.DeliveryType is "standard" or "express");
    }
}
```

The `.When()` predicate receives the full object, so you can check any combination of fields. `.Unless()` is the inverse — apply the rule unless the condition is true.

## Cross-Property Validation

Sometimes a rule involves more than one property. `.Must()` with the full object overload handles this:

```csharp
public class DateRangeRequest
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class DateRangeRequestValidator : AbstractValidator<DateRangeRequest>
{
    public DateRangeRequestValidator()
    {
        RuleFor(x => x.StartDate)
            .LessThan(x => x.EndDate)
            .WithMessage("Start date must be before end date.");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .WithMessage("End date must be after start date.");
    }
}
```

`LessThan` and `GreaterThan` accept a lambda that resolves against the full object — that's the built-in way to compare properties. For more complex cross-field logic, drop down to `.Must()`:

```csharp
RuleFor(x => x.EndDate)
    .Must((request, endDate) => endDate > request.StartDate)
    .WithMessage("End date must be after start date.");
```

## Custom Validators

If you have validation logic you want to reuse across multiple validators, you have two options: a custom rule method (for simple predicates) or a custom `PropertyValidator` (for more complex behaviour).

A custom rule method using `.Must()`:

```csharp
public class ProductCodeValidator : AbstractValidator<CreateOrderRequest>
{
    public ProductCodeValidator()
    {
        RuleFor(x => x.ProductCode)
            .Must(BeAValidProductCode)
            .WithMessage("'{PropertyValue}' is not a recognised product code.");
    }

    private static bool BeAValidProductCode(string code)
    {
        // Product codes are uppercase letters followed by digits: ABC123
        return System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Z]{3}\d{3}$");
    }
}
```

For async validation — checking uniqueness against a database, for example — use `MustAsync`:

```csharp
public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    private readonly IUserRepository _userRepository;

    public CreateUserRequestValidator(IUserRepository userRepository)
    {
        _userRepository = userRepository;

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MustAsync(BeUniqueEmail)
            .WithMessage("An account with that email address already exists.");
    }

    private async Task<bool> BeUniqueEmail(
        string email,
        CancellationToken cancellationToken)
    {
        return !await _userRepository.ExistsAsync(email, cancellationToken);
    }
}
```

Since `CreateUserRequestValidator` has a constructor dependency on `IUserRepository`, it gets resolved from DI automatically — that's another advantage of treating validators as first-class DI objects.

## Testing Validators

Validators are just C# classes. Test them directly without spinning up the web host:

```csharp
public class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    [Fact]
    public async Task ValidRequest_PassesValidation()
    {
        var request = new CreateOrderRequest
        {
            ProductCode = "ABC123",
            Quantity = 5,
            ShippingAddress = "123 Main St"
        };

        var result = await _validator.ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ZeroQuantity_FailsValidation()
    {
        var request = new CreateOrderRequest
        {
            ProductCode = "ABC123",
            Quantity = 0,
            ShippingAddress = "123 Main St"
        };

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Quantity");
    }

    [Fact]
    public async Task EmptyProductCode_FailsWithExpectedMessage()
    {
        var request = new CreateOrderRequest
        {
            ProductCode = "",
            Quantity = 1,
            ShippingAddress = "123 Main St"
        };

        var result = await _validator.ValidateAsync(request);

        Assert.Contains(result.Errors, e =>
            e.PropertyName == "ProductCode" &&
            e.ErrorMessage.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }
}
```

Testing validators in isolation keeps the tests fast and focused. No DI, no HTTP stack, just rule evaluation.

## Worth Knowing

A few things that tend to come up:

**Validation order**: By default, FluentValidation stops at the first failure for each rule chain but continues to the next `RuleFor`. If you want to bail out entirely on the first failure (e.g., to avoid expensive async checks when basic validation already failed), use `ClassLevelCascadeMode = CascadeMode.Stop` in the validator constructor.

**Child validators**: If your model has nested objects, you can compose validators:

```csharp
RuleFor(x => x.BillingAddress)
    .NotNull()
    .SetValidator(new AddressValidator());
```

`SetValidator` runs `AddressValidator` against the `BillingAddress` property, and any failures appear with the nested property path (e.g., `BillingAddress.PostalCode`).

**Collections**: Validate each item in a collection with `RuleForEach`:

```csharp
RuleForEach(x => x.LineItems)
    .SetValidator(new LineItemValidator());
```

Errors appear with indexed paths: `LineItems[0].Quantity`, `LineItems[1].ProductCode`, and so on.

## A Cleaner Way to Handle Validation

FluentValidation's real value isn't just the fluent syntax — it's that validation logic becomes testable, composable, and easy to find. When someone asks "what are the rules for creating an order?", the answer is one class rather than a hunt through model attributes, action filter attributes, and manual checks sprinkled throughout the controller.

The upfront overhead of a separate class per model is worth it. Validation rules tend to grow over time, and having them in a dedicated place makes that growth manageable.
