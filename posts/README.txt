Posts Directory Structure
=========================

Each blog post is a Markdown file in this directory, named using the convention:

    YYYY-MM-DD-descriptive-slug.md

Every post may have a corresponding featured image stored in the images/ subfolder:

    images/descriptive-slug.png

The image filename matches the slug portion of the post filename (without the date
prefix and without the .md extension). For example:

    posts/
    ├── 2026-03-04-repository-pattern-abstracting-data-access.md
    ├── 2026-03-05-decorator-pattern-adding-behavior-without-modification.md
    └── images/
        ├── repository-pattern-abstracting-data-access.png
        └── decorator-pattern-adding-behavior-without-modification.png

The image field in the post's YAML frontmatter references the image by filename:

    ---
    title: My Post Title
    date: 2026-03-04
    tags: dotnet, csharp
    image: repository-pattern-abstracting-data-access.png
    ---
