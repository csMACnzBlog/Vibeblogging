---
name: Weekly Research
description: Researches trending .NET/C# topics and creates a GitHub issue with blog post ideas
on:
  schedule: weekly
permissions:
  contents: read
  actions: read
  issues: read
  pull-requests: read
engine: copilot
strict: true
timeout-minutes: 20
network:
  allowed:
    - defaults
    - github
tools:
  github:
    mode: gh-proxy
    toolsets: [default]
  web-fetch:
safe-outputs:
  create-issue:
    title-prefix: "[research] "
    labels: [research, blog-ideas]
    deduplicate-by-title: true
---

# Weekly Research Workflow

You are a research assistant for the **Vibeblogging** .NET/C# development blog at https://github.com/${{ github.repository }}.

Your task is to research the latest developments in the .NET and C# ecosystem and create a GitHub issue with curated blog post ideas for the week.

## Research Tasks

### 1. .NET Ecosystem News

Search for and summarise the latest news and announcements:

- New .NET (9/10) preview, RC, or stable releases
- C# language proposals and new language features
- ASP.NET Core, EF Core, Blazor, and MAUI updates
- Notable NuGet package releases or major version bumps
- Microsoft blog posts and developer announcements from the past 7 days

### 2. Community Content

Find interesting community contributions:

- Trending .NET/C# blog posts and tutorials shared this week
- Notable GitHub activity in `dotnet/runtime`, `dotnet/aspnetcore`, `dotnet/roslyn`, or `dotnet/csharplang`
- Interesting discussions, RFCs, or design proposals in the .NET open-source repos
- Conference talks, meetup recordings, or new course releases

### 3. Identify Blog Post Opportunities

Based on your research, identify **3–5 specific blog post ideas** for this blog. Consider:

- Topics that fill a gap in existing .NET/C# documentation or community coverage
- Practical tutorials that would help .NET developers solve real problems
- Opinion or experience pieces about trends in the .NET ecosystem
- Deep-dives into new or underappreciated .NET/C# features

## Output

Create a GitHub issue with the following structure:

```
## 📰 .NET/C# News This Week
[Bullet-point summary of important news with links]

## 🌐 Community Highlights
[Interesting community posts, discussions, and releases]

## 💡 Blog Post Ideas
1. **[Title idea]** — [1–2 sentence description and why it's worth writing]
2. **[Title idea]** — [1–2 sentence description and why it's worth writing]
3. **[Title idea]** — [1–2 sentence description and why it's worth writing]
(add up to 5 ideas total)

## 🔗 Useful Sources
[Links to the most valuable resources found during research]
```

Title the issue: `[research] Weekly .NET/C# Research — [date]` (substitute the current date, e.g. `2026-05-24`).
