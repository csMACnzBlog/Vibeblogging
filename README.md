# Vibeblogging

[![Build and Deploy](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/deploy.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/deploy.yml)
[![PR Validation](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/pr-validation.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/pr-validation.yml)
[![CodeQL](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/codeql.yml/badge.svg)](https://github.com/csMACnzBlog/Vibeblogging/actions/workflows/codeql.yml)

A static site generated blog where articles are written in markdown and rendered as beautiful HTML pages. Powered by C# and GitHub Actions.

## 🚀 Features

- **Markdown-Based**: Write posts in simple, readable markdown
- **Static Site Generation**: Fast, secure, and easy to host
- **C# Powered**: Built with .NET and the Markdig library
- **Automated Deployment**: GitHub Actions builds and publishes automatically
- **RSS Feed**: Keep readers updated with new content
- **Clean Design**: Minimal, responsive, and accessible interface
- **Agentic Support**: Copilot agent instructions for content creation

## 📝 Writing Posts

### Create a New Post

1. Create a new markdown file in the `posts/` directory
2. Use the naming convention: `YYYY-MM-DD-descriptive-slug.md`
3. Add frontmatter with title, date, and tags
4. Write your content in markdown

### Post Format

```markdown
---
title: Your Post Title
date: 2026-02-25
tags: tag1, tag2, tag3
image: post-slug.png
---

# Main Heading

Your content here...

## Section Heading

More content...
```

**Featured Images**: Each post should include a featured image. Use the `@image-generator` Copilot agent to create AI-generated images using HuggingFace Inference API. Images are saved to `posts/images/` with the post slug as filename.

### Example

See `posts/2026-02-25-welcome.md` for a complete example.

## 🛠️ Building the Site

### Prerequisites

- .NET 10 SDK or later

### Build and Generate

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Generate the static site
dotnet run --project src/SiteGenerator/SiteGenerator.csproj
```

The generated site will be in the `output/` directory.

### Local Preview

To preview the site locally, you can use any static web server:

```bash
# Using Python
cd output
python -m http.server 8000

# Using Node.js
cd output
npx http-server

# Using .NET
cd output
dotnet serve
```

Then open http://localhost:8000 in your browser.

## 🚢 Deployment

The blog automatically deploys to GitHub Pages when you push to the `main` branch.

### Quality Checks

Every build runs comprehensive quality checks:

- **Unit Tests**: xUnit tests verify core site generator functionality
- **E2E Tests**: Playwright tests validate the generated site
- **Link Checking**: All links are validated using [lychee](https://github.com/lycheeverse/lychee)
- **HTML Validation**: HTML is validated using [html-validate](https://html-validate.org/)
- **Accessibility Testing**: Automated accessibility checks using [pa11y-ci](https://github.com/pa11y/pa11y-ci)
- **Security Scanning**: CodeQL and dependency review protect against vulnerabilities

### Setup GitHub Pages

1. Go to your repository Settings > Pages
2. Set Source to "GitHub Actions"
3. The workflow will automatically build and deploy on push

### Manual Deployment

You can also manually trigger deployment:

1. Go to Actions tab in your repository
2. Select "Build and Deploy" workflow
3. Click "Run workflow"

## 📂 Project Structure

```
Vibeblogging/
├── .copilot/
│   └── agents/               # Agentic instruction files
│       ├── blog-post-writer.md
│       ├── content-manager.md
│       └── image-generator.md
├── .github/
│   └── workflows/
│       └── deploy.yml        # GitHub Actions workflow
├── posts/                    # Markdown blog posts
│   ├── images/              # Blog post featured images
│   └── 2026-02-25-welcome.md
├── scripts/                  # Utility scripts
│   ├── Generate-BlogImage.ps1  # HuggingFace API image generator
│   └── run-a11y-tests.sh
├── src/
│   └── SiteGenerator/        # C# static site generator
│       ├── Program.cs
│       └── SiteGenerator.csproj
├── templates/                # HTML templates
│   ├── index.html           # Homepage template
│   ├── post.html            # Post page template
│   └── styles.css           # Site styles
├── output/                   # Generated static site (gitignored)
├── .gitignore
├── README.md
└── Vibeblogging.sln
```

## 🤖 Agentic Instructions

This repository includes Copilot agent instruction files to assist with content creation:

- **blog-post-writer.md**: Guidelines for writing engaging blog posts
- **content-manager.md**: Instructions for managing and organizing content
- **image-generator.md**: AI-powered image generation using HuggingFace Inference API

### Featured Image Generation

Blog posts can include AI-generated featured images created using HuggingFace Inference API:

**Setup**:
1. Create a free account at [HuggingFace.co](https://huggingface.co/)
2. Generate an API token at [HuggingFace Settings](https://huggingface.co/settings/tokens)
3. Set the `HUGGINGFACE_API_KEY` environment variable
4. Use the `@image-generator` agent to generate images

**Style**: Images are generated in a "pseudo realistic cell-shaded" style with focus and blur effects, featuring everyday scenes or objects (such as office items, household objects, or empty spaces) suitable for technical blog posts.

**Manual Usage**:
```bash
pwsh scripts/Generate-BlogImage.ps1 \
  -PostTitle "Your Post Title" \
  -PostContent "Brief description of themes" \
  -OutputFileName "post-slug.png"
```

See `scripts/README.md` for detailed documentation.

These files help Copilot understand the blog structure and provide better assistance when creating or editing content.

## 🎨 Customization

### Modify Templates

Edit files in the `templates/` directory:

- `index.html`: Homepage layout
- `post.html`: Individual post layout
- `styles.css`: Site-wide styling

### Update Site Generator

Modify `src/SiteGenerator/Program.cs` to customize:

- Markdown processing
- HTML generation
- RSS feed format
- Slug generation
- Post metadata handling

## 📋 Content Guidelines

- Use descriptive titles (50-60 characters ideal)
- Include 2-5 relevant tags per post
- Structure content with clear headings
- Write in a conversational, engaging tone
- Use code blocks with syntax highlighting
- Keep paragraphs concise (3-4 sentences)
- Proofread before publishing

## 🔧 Development

### Running Tests

```bash
dotnet test
```

### Code Formatting

```bash
dotnet format
```

## 📄 License

This project is open source and available under the MIT License.

## 🤝 Contributing

Contributions are welcome! Feel free to:

- Add new features to the site generator
- Improve templates and styling
- Submit blog posts
- Report bugs or suggest improvements

## 📞 Support

For questions or issues, please open an issue in the GitHub repository.

---

Built with ❤️ using C# and .NET
