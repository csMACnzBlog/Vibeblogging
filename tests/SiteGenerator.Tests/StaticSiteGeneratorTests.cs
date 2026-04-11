using System.Text.RegularExpressions;

namespace SiteGenerator.Tests;

public class StaticSiteGeneratorTests
{
    private readonly string _testDir;
    private readonly string _postsDir;
    private readonly string _templatesDir;
    private readonly string _outputDir;

    public StaticSiteGeneratorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SiteGeneratorTests_{Guid.NewGuid()}");
        _postsDir = Path.Combine(_testDir, "posts");
        _templatesDir = Path.Combine(_testDir, "templates");
        _outputDir = Path.Combine(_testDir, "output");
        
        Directory.CreateDirectory(_postsDir);
        Directory.CreateDirectory(_templatesDir);
    }

    [Fact]
    public void Generate_CreatesOutputDirectory()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        Assert.True(Directory.Exists(_outputDir));
    }

    [Fact]
    public void Generate_CreatesIndexHtml()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var indexPath = Path.Combine(_outputDir, "index.html");
        Assert.True(File.Exists(indexPath));
    }

    [Fact]
    public void Generate_CreatesPostHtml()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-test-post.md", "Test Post", "2026-02-25", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postPath = Path.Combine(_outputDir, "test-post.html");
        Assert.True(File.Exists(postPath));
    }

    [Fact]
    public void Generate_CreatesRssFeed()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var rssPath = Path.Combine(_outputDir, "rss.xml");
        Assert.True(File.Exists(rssPath));
    }

    [Fact]
    public void Generate_CreatesSearchPage()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var searchPath = Path.Combine(_outputDir, "search.html");
        Assert.True(File.Exists(searchPath));
    }

    [Fact]
    public void Generate_SearchPageContainsPostMetadata()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-search-test.md", "Search Test Post", "2026-02-25", "dotnet, csharp");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var searchContent = File.ReadAllText(Path.Combine(_outputDir, "search.html"));
        Assert.Contains("Search Test Post", searchContent);
        Assert.Contains("search-test", searchContent);
        Assert.Contains("dotnet", searchContent);
        Assert.Contains("csharp", searchContent);
    }

    [Fact]
    public void Generate_CopiesStylesheet()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var cssPath = Path.Combine(_outputDir, "styles.css");
        Assert.True(File.Exists(cssPath));
    }

    [Fact]
    public void Generate_ProcessesMarkdown()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-markdown-test.md", "Markdown Test", "2026-02-25", "test", "# Heading\n\nSome **bold** text.");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postPath = Path.Combine(_outputDir, "markdown-test.html");
        var content = File.ReadAllText(postPath);
        // The content should contain the processed markdown
        Assert.Contains("Heading", content);
        Assert.Contains("bold", content);
    }

    [Fact]
    public void Generate_ExtractsFrontmatter()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-frontmatter-test.md", "Frontmatter Test", "2026-02-15", "test1, test2");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postPath = Path.Combine(_outputDir, "frontmatter-test.html");
        var content = File.ReadAllText(postPath);
        Assert.Contains("Frontmatter Test", content);
        Assert.Contains("2026-02-15", content);
    }

    [Fact]
    public void Generate_OrdersPostsByDateDescending()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-20-old-post.md", "Old Post", "2026-02-20", "test");
        CreateTestPost("2026-02-25-new-post.md", "New Post", "2026-02-25", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var indexPath = Path.Combine(_outputDir, "index.html");
        var content = File.ReadAllText(indexPath);
        var newPostIndex = content.IndexOf("New Post");
        var oldPostIndex = content.IndexOf("Old Post");
        Assert.True(newPostIndex < oldPostIndex, "Newer post should appear first");
    }

    [Fact]
    public void Generate_HandlesMultiplePosts()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-post1.md", "Post 1", "2026-02-25", "test");
        CreateTestPost("2026-02-24-post2.md", "Post 2", "2026-02-24", "test");
        CreateTestPost("2026-02-23-post3.md", "Post 3", "2026-02-23", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        Assert.True(File.Exists(Path.Combine(_outputDir, "post1.html")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "post2.html")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "post3.html")));
    }

    [Fact]
    public void Generate_HandlesFeaturedImages()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPostWithImage("2026-02-25-image-post.md", "Image Post", "2026-02-25", "test", "test-image.png");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postPath = Path.Combine(_outputDir, "image-post.html");
        var content = File.ReadAllText(postPath);
        Assert.Contains("test-image.png", content);
        Assert.Contains("images/posts/", content);
    }

    [Fact]
    public void Generate_CopiesPostImagesDirectory()
    {
        // Arrange
        SetupTestEnvironment();
        var postImagesDir = Path.Combine(_postsDir, "images");
        Directory.CreateDirectory(postImagesDir);
        File.WriteAllText(Path.Combine(postImagesDir, "test.png"), "fake image content");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var outputImagesDir = Path.Combine(_outputDir, "images", "posts");
        Assert.True(Directory.Exists(outputImagesDir));
        Assert.True(File.Exists(Path.Combine(outputImagesDir, "test.png")));
    }

    [Fact]
    public void Generate_StripsQuotesFromTitle()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPostWithQuotedTitle("2026-02-25-quoted-title.md", "\"Quoted Title: With Colon\"", "2026-02-25", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postPath = Path.Combine(_outputDir, "quoted-title.html");
        var content = File.ReadAllText(postPath);
        // Title should not have surrounding quotes in rendered HTML
        Assert.Contains("<h1>Quoted Title: With Colon</h1>", content);
        Assert.DoesNotContain("<h1>\"Quoted Title: With Colon\"</h1>", content);
    }

    [Fact]
    public void Generate_RemovesDotsFromHeadingIds()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-dotid-test.md", "Dot ID Test", "2026-02-25", "test",
            "## Memoisation vs IMemoryCache in ASP.NET Core\n\nSome content.");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postPath = Path.Combine(_outputDir, "dotid-test.html");
        var content = File.ReadAllText(postPath);
        // ID must not contain dots
        Assert.DoesNotMatch(@"id=""[^""]*\.[^""]*""", content);
        // The heading should still be present
        Assert.Contains("Memoisation vs IMemoryCache in ASP.NET Core", content);
    }

    [Fact]
    public void Generate_WithMoreThanPageSizePosts_CreatesPage2()
    {
        // Arrange
        SetupTestEnvironment();
        for (int i = 1; i <= 16; i++)
        {
            CreateTestPost($"2026-01-{i:D2}-post{i}.md", $"Post {i}", $"2026-01-{i:D2}", "test");
        }
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        Assert.True(File.Exists(Path.Combine(_outputDir, "index.html")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "page2.html")));
    }

    [Fact]
    public void Generate_WithFifteenOrFewerPosts_DoesNotCreatePage2()
    {
        // Arrange
        SetupTestEnvironment();
        for (int i = 1; i <= 15; i++)
        {
            CreateTestPost($"2026-01-{i:D2}-post{i}.md", $"Post {i}", $"2026-01-{i:D2}", "test");
        }
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        Assert.True(File.Exists(Path.Combine(_outputDir, "index.html")));
        Assert.False(File.Exists(Path.Combine(_outputDir, "page2.html")));
    }

    [Fact]
    public void Generate_IndexPageOnlyContainsFirstPagePosts()
    {
        // Arrange
        SetupTestEnvironment();
        for (int i = 1; i <= 16; i++)
        {
            CreateTestPost($"2026-01-{i:D2}-post{i}.md", $"Post {i}", $"2026-01-{i:D2}", "test");
        }
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert: index.html shows only the 15 newest posts (posts 16..2) not post 1
        var indexContent = File.ReadAllText(Path.Combine(_outputDir, "index.html"));
        Assert.Contains("post16.html", indexContent);
        Assert.Contains("post2.html", indexContent);
        Assert.DoesNotContain("post1.html", indexContent);
    }

    [Fact]
    public void Generate_Page2ContainsOlderPosts()
    {
        // Arrange
        SetupTestEnvironment();
        for (int i = 1; i <= 16; i++)
        {
            CreateTestPost($"2026-01-{i:D2}-post{i}.md", $"Post {i}", $"2026-01-{i:D2}", "test");
        }
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert: page2.html contains the oldest post (post 1)
        var page2Content = File.ReadAllText(Path.Combine(_outputDir, "page2.html"));
        Assert.Contains("post1.html", page2Content);
        Assert.DoesNotContain("post16.html", page2Content);
    }

    [Fact]
    public void Generate_IndexPageIncludesOlderPostsLinkWhenMultiplePages()
    {
        // Arrange
        SetupTestEnvironment();
        for (int i = 1; i <= 16; i++)
        {
            CreateTestPost($"2026-01-{i:D2}-post{i}.md", $"Post {i}", $"2026-01-{i:D2}", "test");
        }
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert: page 1 (index.html) has a "Older Posts" link to page2.html
        var indexContent = File.ReadAllText(Path.Combine(_outputDir, "index.html"));
        Assert.Contains("page2.html", indexContent);
        Assert.Contains("Older Posts", indexContent);
    }

    [Fact]
    public void Generate_Page2IncludesNewerPostsLinkBackToIndex()
    {
        // Arrange
        SetupTestEnvironment();
        for (int i = 1; i <= 16; i++)
        {
            CreateTestPost($"2026-01-{i:D2}-post{i}.md", $"Post {i}", $"2026-01-{i:D2}", "test");
        }
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert: page2.html links back to index.html for newer posts
        var page2Content = File.ReadAllText(Path.Combine(_outputDir, "page2.html"));
        Assert.Contains("index.html", page2Content);
        Assert.Contains("Newer Posts", page2Content);
    }

    [Fact]
    public void Generate_NoPaginationWhenOnlyOnePage()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-single-post.md", "Single Post", "2026-02-25", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert: no pagination nav rendered when there is only one page
        var indexContent = File.ReadAllText(Path.Combine(_outputDir, "index.html"));
        Assert.DoesNotContain("<nav class=\"pagination\"", indexContent);
    }


    [Fact]
    public void Generate_CreatesSitemapXml()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var sitemapPath = Path.Combine(_outputDir, "sitemap.xml");
        Assert.True(File.Exists(sitemapPath));
    }

    [Fact]
    public void Generate_SitemapContainsPostUrls()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-sitemap-test.md", "Sitemap Test Post", "2026-02-25", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var sitemapContent = File.ReadAllText(Path.Combine(_outputDir, "sitemap.xml"));
        Assert.Contains("sitemap-test.html", sitemapContent);
        Assert.Contains("<loc>", sitemapContent);
        Assert.Contains("<changefreq>", sitemapContent);
        Assert.Contains("<priority>", sitemapContent);
    }

    [Fact]
    public void Generate_CreatesRobotsTxt()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var robotsPath = Path.Combine(_outputDir, "robots.txt");
        Assert.True(File.Exists(robotsPath));
    }

    [Fact]
    public void Generate_RobotsTxtContainsSitemapReference()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var robotsContent = File.ReadAllText(Path.Combine(_outputDir, "robots.txt"));
        Assert.Contains("Sitemap:", robotsContent);
        Assert.Contains("sitemap.xml", robotsContent);
        Assert.Contains("User-agent: *", robotsContent);
    }

    [Fact]
    public void Generate_PostPageContainsCanonicalUrl()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-canonical-test.md", "Canonical Test", "2026-02-25", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postContent = File.ReadAllText(Path.Combine(_outputDir, "canonical-test.html"));
        Assert.Contains("rel=\"canonical\"", postContent);
        Assert.Contains("canonical-test.html", postContent);
    }

    [Fact]
    public void Generate_PostPageContainsMetaDescription()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-desc-test.md", "Description Test", "2026-02-25", "test", "This is some meaningful content for the description.");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postContent = File.ReadAllText(Path.Combine(_outputDir, "desc-test.html"));
        Assert.Contains("name=\"description\"", postContent);
        Assert.Contains("meaningful content", postContent);
    }

    [Fact]
    public void Generate_PostPageContainsOpenGraphTags()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-og-test.md", "OG Test", "2026-02-25", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postContent = File.ReadAllText(Path.Combine(_outputDir, "og-test.html"));
        Assert.Contains("property=\"og:title\"", postContent);
        Assert.Contains("property=\"og:description\"", postContent);
        Assert.Contains("property=\"og:url\"", postContent);
        Assert.Contains("property=\"og:type\"", postContent);
        Assert.Contains("property=\"og:image\"", postContent);
    }

    [Fact]
    public void Generate_PostPageContainsStructuredData()
    {
        // Arrange
        SetupTestEnvironment();
        CreateTestPost("2026-02-25-ld-test.md", "LD Test", "2026-02-25", "test");
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var postContent = File.ReadAllText(Path.Combine(_outputDir, "ld-test.html"));
        Assert.Contains("application/ld+json", postContent);
        Assert.Contains("BlogPosting", postContent);
    }

    [Fact]
    public void Generate_RssFeedIncludesAllPosts()
    {
        // Arrange
        SetupTestEnvironment();
        for (int i = 1; i <= 12; i++)
        {
            CreateTestPost($"2026-01-{i:D2}-post{i}.md", $"Post {i}", $"2026-01-{i:D2}", "test");
        }
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert: RSS feed should contain all 12 posts, not just 10
        var rssContent = File.ReadAllText(Path.Combine(_outputDir, "rss.xml"));
        Assert.Contains("post11.html", rssContent);
        Assert.Contains("post12.html", rssContent);
    }

    [Fact]
    public void Generate_RssFeedContainsAtomLink()
    {
        // Arrange
        SetupTestEnvironment();
        var generator = CreateGenerator();

        // Act
        generator.Generate();

        // Assert
        var rssContent = File.ReadAllText(Path.Combine(_outputDir, "rss.xml"));
        Assert.Contains("atom:link", rssContent);
        Assert.Contains("rel=\"self\"", rssContent);
    }


    private StaticSiteGenerator CreateGenerator()
    {
        // Save current directory
        var originalDir = Directory.GetCurrentDirectory();
        
        // Change to test directory
        Directory.SetCurrentDirectory(_testDir);
        
        var generator = new StaticSiteGenerator();
        
        // Restore original directory
        Directory.SetCurrentDirectory(originalDir);
        
        return generator;
    }

    private void SetupTestEnvironment()
    {
        // Create template files
        var postTemplate = @"<!DOCTYPE html>
<html>
<head>
    <title>{{TITLE}}</title>
    <link rel=""stylesheet"" href=""styles.css"">
    {{SEO_META}}
</head>
<body>
    <article>
        {{#FEATURED_IMAGE}}<div class=""featured-image"">{{FEATURED_IMAGE}}</div>{{/FEATURED_IMAGE}}
        <h1>{{TITLE}}</h1>
        <time datetime=""{{DATE}}"">{{FORMATTED_DATE}}</time>
        {{#TAGS}}<div class=""tags"">{{TAGS}}</div>{{/TAGS}}
        <div class=""content"">{{CONTENT}}</div>
    </article>
</body>
</html>";

        var indexTemplate = @"<!DOCTYPE html>
<html>
<head>
    <title>Vibeblogging</title>
    <link rel=""stylesheet"" href=""styles.css"">
</head>
<body>
    <h1>Vibeblogging</h1>
    <div class=""posts"">
{{POST_LIST}}
    </div>
    {{PAGINATION}}
</body>
</html>";

        var cssContent = "body { font-family: sans-serif; }";
        var searchTemplate = @"<!DOCTYPE html>
<html>
<head>
    <title>Search</title>
</head>
<body>
    <div id=""results""></div>
    <script>var posts = {{POSTS_JSON}};</script>
</body>
</html>";

        File.WriteAllText(Path.Combine(_templatesDir, "post.html"), postTemplate);
        File.WriteAllText(Path.Combine(_templatesDir, "index.html"), indexTemplate);
        File.WriteAllText(Path.Combine(_templatesDir, "styles.css"), cssContent);
        File.WriteAllText(Path.Combine(_templatesDir, "search.html"), searchTemplate);
    }

    private void CreateTestPost(string filename, string title, string date, string tags, string content = "Test content")
    {
        var frontmatter = $@"---
title: {title}
date: {date}
tags: {tags}
---

{content}";

        File.WriteAllText(Path.Combine(_postsDir, filename), frontmatter);
    }

    private void CreateTestPostWithImage(string filename, string title, string date, string tags, string image, string content = "Test content")
    {
        var frontmatter = $@"---
title: {title}
date: {date}
tags: {tags}
image: {image}
---

{content}";

        File.WriteAllText(Path.Combine(_postsDir, filename), frontmatter);
    }

    private void CreateTestPostWithQuotedTitle(string filename, string title, string date, string tags, string content = "Test content")
    {
        var frontmatter = $@"---
title: {title}
date: {date}
tags: {tags}
---

{content}";

        File.WriteAllText(Path.Combine(_postsDir, filename), frontmatter);
    }
}
