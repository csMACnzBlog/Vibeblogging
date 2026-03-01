# Contributing to Vibeblogging

Thank you for your interest in contributing to Vibeblogging! This document provides guidelines for contributing to the project.

## 🚀 Quick Start

1. Clone the repository
2. Install .NET 10 SDK or later
3. Run `dotnet restore` to restore dependencies
4. Run `dotnet build` to build the project
5. Run `dotnet run --project src/SiteGenerator/SiteGenerator.csproj` to generate the site

## 📝 Writing Blog Posts

### Creating a New Post

1. Create a new markdown file in the `posts/` directory
2. Use the naming convention: `YYYY-MM-DD-descriptive-slug.md`
3. Add frontmatter at the top:

```markdown
---
title: Your Post Title
date: 2026-02-25
tags: tag1, tag2, tag3
image: post-slug.png
---

# Your Content Here
```

4. Write your content using markdown
5. Generate a featured image using the `@image-generator` agent (optional but recommended)
6. Generate the site to preview your changes
7. Commit and push your changes

### Featured Images

Blog posts can include AI-generated featured images:

**Using Copilot Agent** (Recommended):
- Use `@image-generator` agent with your post title and content
- The agent will generate a stylized image using HuggingFace Inference API

**Manual Generation**:
```bash
python scripts/generate_blog_image.py \
  --title "Your Post Title" \
  --content "Brief description of post themes" \
  --scene "A creative scene description with everyday objects" \
  --output "post-slug.png"
```

Requires `HUGGINGFACE_API_KEY` environment variable. See `scripts/README.md` for details.

### Post Guidelines

- **Title**: Clear, descriptive, and engaging (50-60 characters ideal)
- **Date**: Use YYYY-MM-DD format
- **Tags**: Include 2-5 relevant tags, comma-separated
- **Content**: Well-structured with headings, lists, and code blocks
- **Length**: 500-1500 words depending on topic
- **Tone**: Friendly, conversational, yet informative

## 🛠️ Development

### Project Structure

```
Vibeblogging/
├── .copilot/agents/      # Agentic instruction files
│   ├── blog-post-writer.md
│   ├── content-manager.md
│   └── image-generator.md
├── .github/workflows/    # GitHub Actions workflows
├── posts/                # Markdown blog posts
│   └── images/          # Featured images for posts
├── scripts/              # Utility scripts
│   ├── generate_blog_image.py
│   ├── requirements.txt
│   └── run-a11y-tests.sh
├── src/SiteGenerator/    # C# static site generator
├── templates/            # HTML templates and CSS
└── output/              # Generated site (not committed)
```

### Building the Site

```bash
# Build the C# project
dotnet build

# Generate the static site
dotnet run --project src/SiteGenerator/SiteGenerator.csproj

# Preview locally (choose one)
cd output
python3 -m http.server 8000
# or
npx http-server
```

### Modifying Templates

- `templates/index.html` - Homepage layout
- `templates/post.html` - Individual post page layout
- `templates/styles.css` - Site-wide CSS styling

Template variables:
- `{{TITLE}}` - Post title
- `{{DATE}}` - ISO date (YYYY-MM-DD)
- `{{FORMATTED_DATE}}` - Human-readable date
- `{{CONTENT}}` - Rendered HTML content
- `{{POST_LIST}}` - Generated list of posts (index page)
- `{{TAGS}}` - Post tags

### Extending the Generator

The static site generator is in `src/SiteGenerator/Program.cs`. Key classes:

- `StaticSiteGenerator` - Main generator class
- `BlogPost` - Post model with metadata
- Methods:
  - `ParsePosts()` - Read and parse markdown files
  - `GeneratePostPages()` - Create individual post HTML
  - `GenerateIndexPage()` - Create homepage
  - `GenerateRssFeed()` - Generate RSS feed

## 🎨 Styling Guidelines

- Use CSS variables defined in `:root` for colors
- Maintain responsive design (mobile-first approach)
- Keep the design minimal and readable
- Test on multiple screen sizes
- Ensure good contrast for accessibility

## 🧪 Testing

Before submitting changes:

1. Build the project: `dotnet build`
2. Generate the site: `dotnet run --project src/SiteGenerator/SiteGenerator.csproj`
3. Preview the output in a browser
4. Check all pages render correctly
5. Verify links and navigation work
6. Test on different screen sizes
7. Validate HTML and CSS if possible

### Copilot Setup Steps

For GitHub Copilot coding agent, the repository includes an official setup steps workflow that prepares the development environment with all required tools:

**Workflow**: `.github/workflows/copilot-setup-steps.yml`

This workflow follows [GitHub's official Copilot setup steps format](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent/customize-the-agent-environment) and provides:
- .NET 10 SDK
- Python 3.12 (with huggingface_hub, Pillow)
- Node.js 20 (with html-validate, pa11y-ci, http-server)
- PowerShell Core
- Playwright browsers (Chromium)
- All dependencies cached for efficiency

**How it works**: When Copilot coding agent works on a task, it runs these setup steps in an ephemeral GitHub Actions environment before starting. This ensures all project dependencies are installed deterministically.

**Validation**: The workflow runs automatically when modified, so you can verify setup steps work correctly before Copilot uses them.

See `.github/workflows/README.md` for detailed documentation.

## 📦 Submitting Changes

1. Create a new branch from `main`
2. Make your changes
3. Test thoroughly
4. Commit with clear, descriptive messages
5. Push to your fork
6. Open a pull request

### Commit Message Format

- Use clear, descriptive commit messages
- Start with a verb (Add, Update, Fix, Remove, etc.)
- Keep the first line under 72 characters
- Add details in the body if needed

Examples:
```
Add post about C# best practices
Update styling for better mobile experience
Fix RSS feed date formatting
```

## 🐛 Reporting Issues

When reporting issues, please include:

- Description of the problem
- Steps to reproduce
- Expected behavior
- Actual behavior
- Screenshots if applicable
- Environment (OS, .NET version, browser)

## 💡 Feature Requests

We welcome feature requests! Please:

- Check existing issues first
- Describe the feature clearly
- Explain the use case
- Consider implementation complexity

## 📜 Code Style

- Follow C# coding conventions
- Use meaningful variable and method names
- Add comments for complex logic
- Keep methods focused and concise
- Prefer readability over cleverness

## 🤝 Community

- Be respectful and inclusive
- Help others when you can
- Share knowledge and ideas
- Provide constructive feedback
- Follow the code of conduct

## ❓ Questions?

If you have questions:

- Check the README.md
- Review existing issues
- Open a new issue with your question

Thank you for contributing to Vibeblogging! 🎉
