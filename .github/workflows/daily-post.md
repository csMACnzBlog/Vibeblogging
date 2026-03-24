---
description: Generate a daily blog post and open a pull request with the new post
on:
  schedule:
    - cron: "5 0 * * *"
  workflow_dispatch:
  skip-if-match: "is:pr is:open in:title \"[daily-post]\""
permissions:
  contents: read
  issues: read
  pull-requests: read
tools:
  github:
    toolsets: [default]
network:
  allowed:
    - defaults
    - dotnet
    - node
    - python
    - "api-inference.huggingface.co"
secrets:
  HUGGINGFACE_API_KEY: ${{ secrets.HUGGINGFACE_API_KEY }}
steps:
  - name: Export Hugging Face API key
    env:
      HUGGINGFACE_API_KEY: ${{ secrets.HUGGINGFACE_API_KEY }}
    run: echo "HUGGINGFACE_API_KEY=$HUGGINGFACE_API_KEY" >> "$GITHUB_ENV"
  - name: Setup .NET
    uses: actions/setup-dotnet@v4
    with:
      dotnet-version: "10.0.x"
  - name: Setup Python
    uses: actions/setup-python@v5
    with:
      python-version: "3.12"
  - name: Install Python dependencies
    run: pip install -r scripts/requirements.txt
  - name: Setup Node.js
    uses: actions/setup-node@v4
    with:
      node-version: "20"
  - name: Install Node.js validation tools
    run: npm install -g html-validate pa11y-ci http-server
  - name: Restore .NET dependencies
    run: dotnet restore
safe-outputs:
  create-pull-request:
    title-prefix: "[daily-post] "
    labels: [daily-post]
    draft: false
  noop:
---

# Daily Blog Post Generator

You are an AI agent that generates a new blog post for the Vibeblogging site each day. Your task is to identify the next post topic, write the post, generate a featured image, validate the site builds correctly, and open a pull request with the result.

## Step 1: Audit Existing Posts

Read all markdown files in the `/posts` directory. For each post, extract:

- Title and date
- Tags and themes covered
- Series connections (e.g., design patterns series, testing series)

Build a map of what has already been published. Identify series in progress and gaps in coverage.

## Step 2: Choose the Next Topic

Based on the audit, pick **one** topic for today's post. Follow these rules:

- **Never duplicate** an existing post topic
- **Continue active series** where possible (check for series roadmaps in existing posts)
- If no active series needs continuing, pick a new topic from these categories:
  - C# language features
  - .NET ecosystem tools and libraries
  - Architecture and design patterns
  - Testing practices
  - DevOps, CI/CD, and tooling
  - Developer practices and workflows
- The topic must be specific enough for a focused, example-driven post (e.g. "Span\<T\> and Memory\<T\> in C#", not just "Performance")
- The topic must be valuable to a practising .NET / C# developer

## Step 3: Write the Blog Post

Create a new markdown file in `/posts` using the naming convention `YYYY-MM-DD-slug.md` where the date is **today's date**.

### Frontmatter

```yaml
---
title: Your Post Title
date: YYYY-MM-DD
tags: tag1, tag2, tag3
image: post-slug.png
---
```

**Title limit**: The title must not exceed 54 characters. The HTML template appends " - Vibeblogging" (15 chars) and the full `<title>` must stay under 70 characters.

### Writing Style

- **Tone**: Conversational and direct, like explaining to a colleague
- **Contractions**: Use natural contractions (I'll, you'll, that's, it's)
- **Structure**: Start simple, build to complex with progressive examples
- **Paragraphs**: Keep short (2–4 sentences) for readability
- **Code examples**: Always include practical, working C# code with ```csharp fenced blocks
- **Headings**: Use descriptive H2/H3 headers
- **Tags**: Include 2–5 relevant tags (e.g., dotnet, csharp, architecture, testing)

### Markdown Rules

- Escape angle brackets in generic types: use `\<T\>` not `<T>`
- Do not use periods in heading text (they produce invalid HTML IDs)
- Use `dotNET` instead of `.NET` in headings

## Step 4: Generate a Featured Image

Every post **must** have a featured image. Run:

```bash
python scripts/generate_blog_image.py \
  --title "Post Title" \
  --content "Brief description of the post themes" \
  --scene "A creative scene with everyday objects" \
  --output "post-slug.png"
```

Verify the image file exists at `posts/images/<post-slug>.png`. If the script creates a nested path, move the file to the correct location.

If image generation fails (e.g., missing API key), still proceed with the post but note the failure in the pull request description.

## Step 5: Validate the Site

Build and validate:

```bash
dotnet build --configuration Release
dotnet run --project src/SiteGenerator/SiteGenerator.csproj --no-build --configuration Release
html-validate 'output/**/*.html'
bash scripts/run-a11y-tests.sh
```

Fix any validation errors in the post before proceeding. Common fixes:

- Shorten the title if html-validate reports `long-title`
- Escape `<T>` as `\<T>` if html-validate reports element errors
- Remove periods from headings if html-validate reports `valid-id` errors

## Step 6: Open a Pull Request

Use the `create-pull-request` safe output to open a PR with:

- The new post markdown file
- The generated featured image
- Any other changed files needed for the site to build

The PR title should describe the post topic (the `[daily-post]` prefix is added automatically).

## When There Is Nothing To Do

If you determine that no new post should be created today (e.g., all reasonable topics are exhausted), call the `noop` safe output with a clear explanation of why no post was generated.
