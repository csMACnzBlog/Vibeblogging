# GitHub Actions Workflows

This directory contains GitHub Actions workflows for the Vibeblogging project.

## Workflows

### Build and Deploy (`deploy.yml`)
[![Build and Deploy](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/deploy.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/deploy.yml)

**Trigger**: Push to `main` branch or manual workflow dispatch

**Purpose**: Builds, tests, and deploys the blog to GitHub Pages

**Steps**:
1. Build and Test Job:
   - Checkout code
   - Setup .NET 10.0
   - Cache NuGet packages for faster builds
   - Restore dependencies
   - Build in Release configuration
   - Run unit tests (xUnit)
   - Generate static site
   - Cache Playwright browsers
   - Install Playwright browsers (if not cached)
   - Run E2E tests (Playwright)
   - Upload test results as artifacts
   - Prepare site for GitHub Pages

2. Deploy Job:
   - Deploy to GitHub Pages

**Improvements Made**:
- ✅ NuGet package caching for faster builds
- ✅ Playwright browser caching to avoid re-downloads
- ✅ Release configuration builds
- ✅ Test result artifact uploads
- ✅ Build artifact uploads for debugging
- ✅ Timeout limits (15 minutes)
- ✅ Consolidated build and test into single job
- ✅ Additional permissions for artifact uploads

### PR Validation (`pr-validation.yml`)
[![PR Validation](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/pr-validation.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/pr-validation.yml)

**Trigger**: Pull requests to `main` branch

**Purpose**: Validates code changes before merging

**Steps**:
1. Checkout code
2. Setup .NET 10.0
3. Cache NuGet packages
4. Restore dependencies
5. Build in Release configuration
6. Run unit tests with test result logging
7. Generate static site
8. Cache Playwright browsers
9. Install Playwright browsers (if not cached)
10. Run E2E tests with test result logging
11. Upload test results as artifacts
12. Upload generated site preview
13. Comment on PR with build status

**Improvements Made**:
- ✅ NuGet package caching for faster builds
- ✅ Playwright browser caching
- ✅ Release configuration builds
- ✅ Test result logging and artifact uploads
- ✅ Generated site artifact with PR number
- ✅ PR commenting with build status
- ✅ Timeout limits (15 minutes)
- ✅ Enhanced permissions for PR comments and checks

### CodeQL Security Scan (`codeql.yml`)
[![CodeQL](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/codeql.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/codeql.yml)

**Trigger**: 
- Push to `main` branch
- Pull requests to `main` branch
- Weekly schedule (Mondays at midnight UTC)

**Purpose**: Performs automated security scanning and code quality analysis

**Steps**:
1. Checkout code
2. Initialize CodeQL for C#
3. Setup .NET 10.0
4. Cache NuGet packages
5. Restore dependencies
6. Build in Release configuration
7. Perform CodeQL analysis

**Features**:
- Security vulnerability detection
- Code quality checks
- Weekly scheduled scans
- Security-and-quality query suite

### Dependency Review (`dependency-review.yml`)
[![Dependency Review](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/dependency-review.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/dependency-review.yml)

**Trigger**: Pull requests to `main` branch

**Purpose**: Reviews dependency changes for security vulnerabilities

**Steps**:
1. Checkout code
2. Run dependency review action
3. Comment summary in PR

**Features**:
- Fails on moderate or higher severity vulnerabilities
- Automatic PR comments with findings
- License compliance checks

### Copilot Agent Environment (`copilot-agent.yml`)
[![Copilot Agent Environment](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/copilot-agent.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/copilot-agent.yml)

**Trigger**: Manual workflow dispatch

**Purpose**: Provides a standardized environment for GitHub Copilot agents with all required development tools pre-installed

**Tools Installed**:
- ✅ .NET 10 SDK
- ✅ Python 3.12 (with huggingface_hub, Pillow)
- ✅ Node.js 20 (with html-validate, pa11y-ci, http-server)
- ✅ PowerShell Core (pwsh)
- ✅ Playwright browsers (Chromium)
- ✅ NuGet packages (cached)

**Steps**:
1. Checkout code
2. Setup .NET 10.0 with NuGet caching
3. Setup Python 3.12 with pip caching
4. Install Python dependencies (huggingface_hub, Pillow)
5. Setup Node.js 20 with npm caching
6. Install Node.js validation tools globally
7. Verify PowerShell availability
8. Restore .NET dependencies
9. Build solution in Release configuration
10. Generate static site
11. Cache and install Playwright browsers
12. Run tests (optional, controlled by input)
13. Generate environment summary
14. Upload artifacts (generated site, test results)

**Options**:
- `skip_tests`: Set to 'true' to skip running tests for faster setup (default: 'false')

**Use Cases**:
- Testing Copilot agent workflows before deployment
- Validating that all tools are properly installed
- Troubleshooting environment issues
- Providing a clean environment for Copilot operations

**Output Artifacts**:
- `generated-site`: The built static site in output/ directory
- `test-results`: Test results and Playwright screenshots (if tests are run)

## Key Improvements Summary

### Performance Optimizations
1. **NuGet Package Caching**: Reduces build time by caching restored packages
2. **Playwright Browser Caching**: Avoids re-downloading browsers on every run
3. **Consolidated Jobs**: Merged duplicate build/test steps in deploy workflow
4. **Release Configuration**: Uses optimized builds instead of Debug

### Reliability & Debugging
1. **Test Result Artifacts**: Preserves test results for analysis
2. **Build Artifacts**: Uploads generated site for inspection
3. **Timeout Limits**: Prevents stuck workflows (15 min for build/test, 10 min for deploy)
4. **Always Upload**: Test results uploaded even on failure

### Security & Quality
1. **CodeQL Integration**: Automated security scanning
2. **Dependency Review**: Checks for vulnerable dependencies
3. **Weekly Security Scans**: Proactive vulnerability detection
4. **Enhanced Permissions**: Properly scoped permissions per workflow

### Developer Experience
1. **PR Comments**: Automatic build status notifications
2. **PR Artifacts**: Generated site preview available for each PR
3. **Test Logging**: TRX format test results for better reporting
4. **Clear Job Names**: Improved readability and debugging

## Cache Management

### NuGet Cache
- **Path**: `~/.nuget/packages`
- **Key**: OS + hash of all .csproj files
- **Restore Key**: OS + nuget (fallback)
- **Invalidation**: Automatic when project files change

### Playwright Cache
- **Path**: `~/.cache/ms-playwright`
- **Key**: OS + hash of Playwright test project file
- **Restore Key**: OS + playwright (fallback)
- **Invalidation**: Automatic when Playwright version changes

## Adding Badges to README

Add these badges to your README.md:

```markdown
[![Build and Deploy](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/deploy.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/deploy.yml)
[![PR Validation](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/pr-validation.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/pr-validation.yml)
[![CodeQL](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/codeql.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/codeql.yml)
[![Copilot Agent Environment](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/copilot-agent.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/copilot-agent.yml)
```

## Troubleshooting

### Cache Issues
If caching causes problems, you can:
1. Clear caches from Actions settings
2. Update cache keys in workflow files
3. Temporarily disable caching to debug

### Test Failures
Test results are available as artifacts:
1. Go to the failed workflow run
2. Download "test-results" artifact
3. Open .trx files in Visual Studio or test explorer

### Build Artifacts
Generated site is available for debugging:
1. Go to workflow run
2. Download "generated-site" or "generated-site-pr-X" artifact
3. Extract and serve locally to inspect

## Future Enhancements

Potential future improvements:
- [ ] Code coverage reporting with Coverlet
- [ ] Performance benchmarking
- [ ] Lighthouse CI for performance scores
- [ ] Automated changelog generation
- [ ] Release automation
- [ ] Link checking for generated site
- [ ] HTML validation
- [ ] Accessibility testing
