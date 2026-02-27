# Copilot Repository Instructions

## Project Overview

Vibeblogging is a static site generated blog where articles are written in markdown and rendered as beautiful HTML pages. The project is built with .NET 10 and C# using a custom static site generator that converts markdown to HTML using the Markdig library.

**Key Technologies:**
- Language: C# (.NET 10+)
- Markdown Parser: Markdig
- Testing: xUnit for unit tests, Playwright for E2E tests
- CI/CD: GitHub Actions
- Hosting: GitHub Pages

## Coding Standards

### C# Conventions
- **Language Version**: C# 10+ features are available
- **Naming**: Follow standard C# naming conventions (PascalCase for public members, camelCase for private fields)
- **Code Style**: Follow standard .NET coding conventions
- **Null Handling**: Use null-conditional operators (?.) and null-coalescing operators (??)
- **LINQ**: Use LINQ for collection operations where it improves readability
- **Comments**: Add comments only when necessary to explain complex logic; prefer self-documenting code

### Project Structure
```
Vibeblogging/
├── .copilot/agents/       # Specialized agent instructions (blog-post-writer, content-manager, image-generator)
├── .github/workflows/      # CI/CD workflows
├── posts/                  # Markdown blog posts (YYYY-MM-DD-slug.md format)
│   └── images/            # Featured images for blog posts
├── scripts/                # Utility scripts (image generation, testing)
├── src/SiteGenerator/      # C# static site generator
├── templates/              # HTML templates and CSS
├── tests/                  # Unit and E2E tests
└── output/                # Generated site (gitignored)
```

## Blog Post Guidelines

### Frontmatter Format
Every blog post must include YAML frontmatter:
```yaml
---
title: Your Post Title
date: YYYY-MM-DD
tags: tag1, tag2, tag3
image: post-slug.png
---
```

**Note**: The `image` field is optional but recommended. Use the `@image-generator` agent to create AI-generated featured images.

### File Naming Convention
- Format: `YYYY-MM-DD-descriptive-slug.md`
- Location: `/posts` directory
- Example: `2026-02-25-getting-started-with-dotnet.md`

### Writing Style
- **Tone**: Conversational and direct, like explaining to a colleague
- **Contractions**: Use natural contractions (I'll, you'll, that's, it's)
- **Structure**: Start simple, build to complex with progressive examples
- **Paragraphs**: Keep short (2-4 sentences) for readability
- **Code Examples**: Always include practical, working code with proper syntax highlighting
- **Headings**: Use descriptive H2/H3 headers to organize content
- **Tags**: Include 2-5 relevant technical tags (e.g., dotnet, csharp, conference, architecture)

### Common Tags
- Technology: `dotnet`, `dotnetcore`, `csharp`, `c-sharp`, `aspnetcore`
- Event: `conference`, `review`
- Content: `tutorial`, `opinion`, `architecture`, `testing`, `deployment`

## Building and Testing

### Build Commands
```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build --configuration Release

# Generate the static site
dotnet run --project src/SiteGenerator/SiteGenerator.csproj

# Run all tests
dotnet test
```

### Testing Infrastructure
- **Unit Tests**: xUnit tests in `tests/SiteGenerator.Tests/` verify core functionality
- **E2E Tests**: Playwright tests in `tests/Site.PlaywrightTests/` verify the generated site
- **Playwright Setup**: Run `pwsh tests/Site.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install chromium` before E2E tests
- **Test Execution**: Always use `--configuration Release` for builds matching production

### Caching
- NuGet packages cached at `~/.nuget/packages`
- Playwright browsers cached at `~/.cache/ms-playwright`

## Template System

### Available Templates
- `templates/index.html` - Homepage layout
- `templates/post.html` - Individual post page layout
- `templates/styles.css` - Site-wide styling

### Template Variables
- `{{TITLE}}` - Post title
- `{{DATE}}` - ISO date (YYYY-MM-DD)
- `{{FORMATTED_DATE}}` - Human-readable date
- `{{CONTENT}}` - Rendered HTML content
- `{{POST_LIST}}` - Generated list of posts (index page)
- `{{TAGS}}` - Post tags

## GitHub Actions Workflows

### Deployment
- Workflow: `.github/workflows/deploy.yml`
- Triggers: Push to `main` branch
- Actions: Build, test, generate site, deploy to GitHub Pages

### PR Validation
- Workflow: `.github/workflows/pr-validation.yml`
- Triggers: Pull requests
- Actions: Build and run all tests

### Security Scanning
- **CodeQL**: Runs on all PRs, pushes to main, and weekly (Mondays)
- **Dependency Review**: Blocks PRs with moderate+ severity vulnerabilities

## Development Workflow

### Making Changes
1. **Code Changes**: Edit files in `src/SiteGenerator/`
2. **Template Changes**: Modify files in `templates/`
3. **New Posts**: Create markdown files in `posts/`
4. **Testing**: Run unit tests (`dotnet test`) and E2E tests after site generation
5. **Local Preview**: Generate site and serve from `output/` directory

### Quality Checks
- Build must succeed with `--configuration Release`
- All tests must pass
- Generated site must render correctly
- Code should follow C# conventions

## Copilot Agent Instructions

Specialized agents are available in `.copilot/agents/`:

### blog-post-writer.md
Use for creating new blog posts. The agent:
- Follows the author's conversational writing style
- Uses progressive examples (simple to complex)
- Includes proper frontmatter and file naming
- Maintains consistent tone with contractions and parenthetical asides

### content-manager.md
Use for organizing and maintaining blog content. The agent:
- Manages post organization and categorization
- Reviews posts for quality and consistency
- Updates templates and site configuration
- Ensures SEO optimization

### image-generator.md
Use for generating featured images for blog posts. The agent:
- Creates AI-generated images using HuggingFace Inference API
- Produces pseudo realistic cell-shaded style with focus and blur effects
- Generates images using everyday scenes and objects (no people, animals, or geometric shapes)
- Handles image prompt construction and API integration
- Requires `HUGGINGFACE_API_KEY` environment variable

To generate an image, use `@image-generator` with the post title and key themes.

## Best Practices

### When Adding New Features
1. Keep the static site generator simple and focused
2. Maintain backward compatibility with existing posts
3. Add tests for new functionality
4. Update documentation and templates as needed
5. Test the generated site locally before pushing

### When Writing Blog Posts
1. Follow the established writing style (conversational, direct, progressive)
2. Include working code examples with proper syntax highlighting
3. Use descriptive headings and short paragraphs
4. Add relevant tags (2-5 per post)
5. Proofread and test rendering before committing

### When Modifying Templates
1. Maintain responsive design
2. Keep the design minimal and readable
3. Test on multiple screen sizes
4. Ensure accessibility (contrast, semantic HTML)
5. Validate HTML and CSS

## Common Tasks

### Create a New Blog Post
```bash
# Let Copilot assist by mentioning @blog-post-writer
# Or manually create: posts/YYYY-MM-DD-title-slug.md
```

### Generate a Featured Image
```bash
# Use the image-generator agent (recommended)
# @image-generator with post title and themes

# Or manually run the script
python scripts/generate_blog_image.py \
  --title "Your Post Title" \
  --content "Brief description of themes" \
  --output "post-slug.png"
```

Requires `HUGGINGFACE_API_KEY` environment variable. See `scripts/README.md` for details.

### Test Changes
```bash
dotnet build --configuration Release
dotnet test
dotnet run --project src/SiteGenerator/SiteGenerator.csproj
# Preview: cd output && python -m http.server 8000
```

### Debug Build Issues
- Check .NET version: `dotnet --version` (should be 10+)
- Clear build artifacts: `dotnet clean`
- Restore packages: `dotnet restore`
- Check for missing dependencies in `.csproj` files

## Notes for Copilot

- **Preserve Existing Patterns**: The blog has an established style and structure - maintain it
- **Ask for Clarification**: If requirements are ambiguous, ask before making changes
- **Test Thoroughly**: Always build and test changes before considering them complete
- **Minimal Changes**: Make the smallest possible changes to achieve the goal
- **Document Changes**: Update relevant documentation when adding features
- **Security**: Never commit secrets; use GitHub Actions secrets for sensitive data
- **Dependencies**: Only add new dependencies if absolutely necessary

## References

- **Main Documentation**: `README.md` - Project overview and getting started
- **Contributing Guide**: `CONTRIBUTING.md` - Detailed contribution guidelines
- **Test Documentation**: `tests/README.md` - Testing infrastructure details
- **Agent Instructions**: `.copilot/agents/` - Specialized agent guidelines
