# CI/CD Quality Checks

This document describes the automated quality checks that run on every build and pull request.

## Overview

The Vibeblogging CI/CD pipeline includes comprehensive quality checks to ensure:
- All links in the generated site are valid
- HTML markup is well-formed and standards-compliant
- The site meets accessibility standards
- Code is secure and free of vulnerabilities

## Quality Checks

### 1. Link Checking

**Tool**: [lychee](https://github.com/lycheeverse/lychee)

**What it does**: Validates all links in the generated HTML files to ensure they are not broken.

**Configuration**: 
- Runs via the `lychee-action` GitHub Action
- Checks all `*.html` files in the `output/` directory
- Excludes `mailto:` links
- Uses the output directory as the base for relative links
- Fails the build if broken links are found

**Command**: Automatically handled by `lycheeverse/lychee-action@v2.0.0`

### 2. HTML Validation

**Tool**: [html-validate](https://html-validate.org/)

**What it does**: Validates HTML markup against W3C standards and best practices.

**Configuration**: `.htmlvalidate.json`
```json
{
  "extends": ["html-validate:recommended"],
  "rules": {
    "no-trailing-whitespace": "off"
  }
}
```

**Customization**: The `no-trailing-whitespace` rule is disabled because the markdown-to-HTML conversion can introduce trailing whitespace that doesn't affect rendering.

**Command**: 
```bash
html-validate 'output/**/*.html'
```

### 3. Accessibility Testing

**Tool**: [pa11y-ci](https://github.com/pa11y/pa11y-ci)

**What it does**: Runs automated accessibility checks using axe-core and HTML CodeSniffer to ensure the site is accessible to users with disabilities.

**Configuration**: `.pa11yci.json`
```json
{
  "defaults": {
    "timeout": 10000,
    "threshold": 10,
    "chromeLaunchConfig": {
      "args": ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
    }
  }
}
```

**Notes**:
- Tests run against a local HTTP server to properly evaluate JavaScript and CSS
- Uses a threshold of 10 errors to allow the build to pass while still reporting issues
- Chrome is launched with `--no-sandbox` and other flags required for CI environments
- Automatically discovers and tests all HTML files in the output directory

**Script**: `scripts/run-a11y-tests.sh`

The script:
1. Starts a local HTTP server on port 8080
2. Discovers all HTML files in the output directory
3. Runs pa11y-ci against each page
4. Reports accessibility issues
5. Stops the HTTP server

**Command**: 
```bash
bash scripts/run-a11y-tests.sh
```

### 4. Unit Tests

**Tool**: xUnit

**What it does**: Tests the core site generator functionality.

**Command**: 
```bash
dotnet test tests/SiteGenerator.Tests/SiteGenerator.Tests.csproj --configuration Release
```

### 5. E2E Tests

**Tool**: Playwright

**What it does**: Tests the generated site in a real browser to ensure it renders correctly.

**Command**: 
```bash
dotnet test tests/Site.PlaywrightTests/Site.PlaywrightTests.csproj --configuration Release
```

### 6. Security Scanning

**Tools**: 
- [CodeQL](https://codeql.github.com/) - Static code analysis
- [Dependency Review](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/about-dependency-review) - Dependency vulnerability scanning

**What they do**: Scan code and dependencies for security vulnerabilities.

**Configuration**: 
- `.github/workflows/codeql.yml` - Runs on PRs, pushes to main, and weekly
- `.github/workflows/dependency-review.yml` - Blocks PRs with moderate+ severity vulnerabilities

## Running Checks Locally

### Prerequisites

Install Node.js and the required tools:

```bash
npm install -g html-validate pa11y-ci http-server
```

### Run All Checks

```bash
# Build and generate the site
dotnet restore
dotnet build --configuration Release
dotnet run --project src/SiteGenerator/SiteGenerator.csproj --no-build --configuration Release

# Run unit tests
dotnet test tests/SiteGenerator.Tests/SiteGenerator.Tests.csproj --configuration Release

# Run HTML validation
html-validate 'output/**/*.html'

# Run accessibility tests
bash scripts/run-a11y-tests.sh

# Note: Link checking with lychee requires installing lychee separately
# See: https://github.com/lycheeverse/lychee#installation
```

### Run Individual Checks

#### HTML Validation
```bash
html-validate 'output/**/*.html'
```

#### Accessibility Testing
```bash
bash scripts/run-a11y-tests.sh
```

Or manually:
```bash
# Start server
cd output && npx http-server -p 8080 &
SERVER_PID=$!

# Run tests
cd ..
npx pa11y-ci --config .pa11yci.json http://localhost:8080/index.html

# Stop server
kill $SERVER_PID
```

## Troubleshooting

### HTML Validation Errors

If you encounter HTML validation errors:

1. Review the error message - it will show the file, line number, and issue
2. Check if the issue is in a template file (`templates/*.html`) or generated content
3. If it's from markdown content, fix the markdown source file
4. If a rule is too strict for your use case, you can disable it in `.htmlvalidate.json`

### Accessibility Errors

If you encounter accessibility errors:

1. Review the error message - it will show the specific WCAG guideline violated
2. Common issues include:
   - Insufficient color contrast
   - Missing alt text on images
   - Missing form labels
   - Improper heading hierarchy
3. Fix the issue in the template or markdown source
4. If the threshold is too strict, you can adjust it in `.pa11yci.json`

### Link Checking Errors

If you encounter broken link errors:

1. Review the lychee output to see which links are broken
2. Fix broken external links or update the content
3. For expected failures (e.g., auth-protected links), you can exclude them in the workflow args

## Configuration Files

### `.htmlvalidate.json`
Controls HTML validation rules and behavior.

### `.pa11yci.json`
Controls accessibility testing behavior, including:
- Timeout settings
- Error threshold
- Chrome launch configuration
- Test runners to use

### `scripts/run-a11y-tests.sh`
Automates the process of starting a server, running tests, and cleanup.

## CI/CD Integration

These checks run automatically:

- **On Pull Requests**: via `.github/workflows/pr-validation.yml`
- **On Push to Main**: via `.github/workflows/deploy.yml`

The checks run in this order:
1. Build and unit tests
2. Site generation
3. Playwright E2E tests
4. Link checking
5. HTML validation
6. Accessibility testing

If any check fails, the workflow stops and the deployment is prevented.

## Best Practices

1. **Run checks locally** before pushing to catch issues early
2. **Fix issues at the source** - templates or markdown, not generated HTML
3. **Keep configuration files in sync** if you update them
4. **Review accessibility issues** even if the build passes (due to threshold)
5. **Monitor CI logs** for warnings and errors

## Future Enhancements

Potential improvements to consider:

- Performance testing (Lighthouse CI)
- Visual regression testing
- Broken link monitoring in production
- Automated fixes for common issues
- Integration with external accessibility services
- WCAG compliance level configuration

## Resources

- [lychee Documentation](https://github.com/lycheeverse/lychee)
- [html-validate Documentation](https://html-validate.org/)
- [pa11y Documentation](https://pa11y.org/)
- [WCAG Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
