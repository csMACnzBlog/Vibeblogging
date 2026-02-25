# Tests

This directory contains tests for the Vibeblogging static site generator.

## Test Framework

Both test projects use **xUnit v3** (package `xunit.v3` version 3.0.1).

## Test Projects

### SiteGenerator.Tests
xUnit tests for the SiteGenerator application. These tests verify:
- Site generation functionality
- Markdown processing
- Frontmatter parsing
- Post ordering
- RSS feed generation
- File operations

**Run tests:**
```bash
dotnet test tests/SiteGenerator.Tests/SiteGenerator.Tests.csproj
```

### Site.PlaywrightTests
xUnit + Playwright E2E tests for the generated static site. These tests verify:
- Index page loads correctly
- Stylesheets are applied
- Posts are listed
- Post pages are accessible
- RSS feed is generated correctly

**Run tests:**
```bash
# Generate the site first
dotnet run --project src/SiteGenerator/SiteGenerator.csproj

# Install Playwright browsers (first time only)
pwsh tests/Site.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install chromium

# Run tests
dotnet test tests/Site.PlaywrightTests/Site.PlaywrightTests.csproj
```

## Running All Tests

To run all tests:
```bash
dotnet test
```

## CI/CD Integration

Tests are automatically run:
- On every push to the `main` branch (before deployment)
- On every pull request to `main`

See `.github/workflows/deploy.yml` and `.github/workflows/pr-validation.yml` for details.
