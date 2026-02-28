using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace SiteGenerator;

class Program
{
    static void Main(string[] args)
    {
        var generator = new StaticSiteGenerator();
        generator.Generate();
        Console.WriteLine("Site generated successfully!");
    }
}

public class StaticSiteGenerator
{
    private readonly string _postsDir;
    private readonly string _templatesDir;
    private readonly string _outputDir;
    private readonly MarkdownPipeline _pipeline;

    public StaticSiteGenerator()
    {
        var baseDir = FindRepositoryRoot();
        _postsDir = Path.Combine(baseDir, "posts");
        _templatesDir = Path.Combine(baseDir, "templates");
        _outputDir = Path.Combine(baseDir, "output");
        _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    }

    private string FindRepositoryRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                Directory.Exists(Path.Combine(current, "posts")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    public void Generate()
    {
        Console.WriteLine("Generating static site...");
        
        // Ensure output directory exists and is clean
        if (Directory.Exists(_outputDir))
        {
            Directory.Delete(_outputDir, true);
        }
        Directory.CreateDirectory(_outputDir);

        // Parse all posts
        var posts = ParsePosts();
        
        // Generate individual post pages
        GeneratePostPages(posts);
        
        // Generate index page
        GenerateIndexPage(posts);
        
        // Generate RSS feed
        GenerateRssFeed(posts);
        
        // Copy CSS
        CopyStyles();
        
        Console.WriteLine($"Generated {posts.Count} posts");
    }

    private List<BlogPost> ParsePosts()
    {
        var posts = new List<BlogPost>();
        
        if (!Directory.Exists(_postsDir))
        {
            Console.WriteLine($"Posts directory not found: {_postsDir}");
            return posts;
        }

        var markdownFiles = Directory.GetFiles(_postsDir, "*.md");
        
        foreach (var file in markdownFiles)
        {
            try
            {
                var post = ParsePost(file);
                posts.Add(post);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {file}: {ex.Message}");
            }
        }
        
        return posts.OrderByDescending(p => p.Date).ToList();
    }

    private BlogPost ParsePost(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var post = new BlogPost { FileName = Path.GetFileName(filePath) };
        
        // Extract frontmatter
        var frontmatterMatch = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline);
        
        if (frontmatterMatch.Success)
        {
            var frontmatter = frontmatterMatch.Groups[1].Value;
            ParseFrontmatter(post, frontmatter);
            content = content.Substring(frontmatterMatch.Length);
        }
        
        // Convert markdown to HTML
        post.Content = Markdown.ToHtml(content, _pipeline);
        
        // Generate slug from filename or title
        post.Slug = GenerateSlug(post.FileName);
        
        return post;
    }

    private void ParseFrontmatter(BlogPost post, string frontmatter)
    {
        var lines = frontmatter.Split('\n');
        
        foreach (var line in lines)
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim().ToLower();
                var value = parts[1].Trim();
                
                // Strip surrounding quotes from value if present
                if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                {
                    value = value.Substring(1, value.Length - 2);
                }
                
                switch (key)
                {
                    case "title":
                        post.Title = value;
                        break;
                    case "date":
                        if (DateTime.TryParse(value, out var date))
                        {
                            post.Date = date;
                        }
                        break;
                    case "tags":
                        post.Tags = value.Split(',').Select(t => t.Trim()).ToList();
                        break;
                    case "image":
                        post.FeaturedImage = value;
                        break;
                }
            }
        }
        
        // Set defaults if not provided
        if (string.IsNullOrEmpty(post.Title))
        {
            post.Title = Path.GetFileNameWithoutExtension(post.FileName);
        }
        
        if (post.Date == default)
        {
            post.Date = DateTime.Now;
        }
    }

    private string GenerateSlug(string fileName)
    {
        var slug = Path.GetFileNameWithoutExtension(fileName);
        slug = Regex.Replace(slug, @"^\d{4}-\d{2}-\d{2}-", ""); // Remove date prefix
        slug = slug.ToLower();
        slug = Regex.Replace(slug, @"[^a-z0-9-]", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');
        return slug;
    }

    private void GeneratePostPages(List<BlogPost> posts)
    {
        var template = File.ReadAllText(Path.Combine(_templatesDir, "post.html"));
        
        foreach (var post in posts)
        {
            var html = template
                .Replace("{{TITLE}}", post.Title)
                .Replace("{{DATE}}", post.Date.ToString("yyyy-MM-dd"))
                .Replace("{{FORMATTED_DATE}}", post.Date.ToString("MMMM dd, yyyy"))
                .Replace("{{CONTENT}}", post.Content);
            
            // Handle featured image
            if (!string.IsNullOrEmpty(post.FeaturedImage))
            {
                var imageHtml = $"<div class=\"post-featured-image\"><img src=\"images/posts/{post.FeaturedImage}\" alt=\"{EscapeHtmlAttribute(post.Title)}\" class=\"featured-image\"></div>";
                html = Regex.Replace(html, @"\{\{#FEATURED_IMAGE\}\}.*?\{\{/FEATURED_IMAGE\}\}", 
                    imageHtml, RegexOptions.Singleline);
            }
            else
            {
                html = Regex.Replace(html, @"\{\{#FEATURED_IMAGE\}\}.*?\{\{/FEATURED_IMAGE\}\}", "", RegexOptions.Singleline);
            }
            
            // Handle tags
            if (post.Tags.Any())
            {
                var tags = string.Join(", ", post.Tags);
                html = Regex.Replace(html, @"\{\{#TAGS\}\}.*?\{\{/TAGS\}\}", 
                    $"<span class=\"tags\">{tags}</span>", RegexOptions.Singleline);
            }
            else
            {
                html = Regex.Replace(html, @"\{\{#TAGS\}\}.*?\{\{/TAGS\}\}", "", RegexOptions.Singleline);
            }
            
            var outputPath = Path.Combine(_outputDir, $"{post.Slug}.html");
            File.WriteAllText(outputPath, html);
        }
    }

    private void GenerateIndexPage(List<BlogPost> posts)
    {
        var template = File.ReadAllText(Path.Combine(_templatesDir, "index.html"));
        
        var postList = new StringBuilder();
        foreach (var post in posts)
        {
            var excerpt = GetExcerpt(post.Content);
            postList.AppendLine("<div class=\"post-item\">");
            
            // Add featured image if available
            if (!string.IsNullOrEmpty(post.FeaturedImage))
            {
                postList.AppendLine($"  <img src=\"images/posts/{post.FeaturedImage}\" alt=\"{EscapeHtmlAttribute(post.Title)}\" class=\"post-thumbnail\">");
            }
            
            postList.AppendLine("  <div class=\"post-item-content\">");
            postList.AppendLine($"    <h2><a href=\"{post.Slug}.html\">{post.Title}</a></h2>");
            postList.AppendLine($"    <div class=\"post-meta\">");
            postList.AppendLine($"      <time datetime=\"{post.Date:yyyy-MM-dd}\">{post.Date:MMMM dd, yyyy}</time>");
            if (post.Tags.Any())
            {
                postList.AppendLine($"      <span class=\"tags\">{string.Join(", ", post.Tags)}</span>");
            }
            postList.AppendLine($"    </div>");
            postList.AppendLine($"    <div class=\"post-excerpt\">{excerpt}</div>");
            postList.AppendLine("  </div>");
            postList.AppendLine("</div>");
        }
        
        var html = template.Replace("{{POST_LIST}}", postList.ToString());
        File.WriteAllText(Path.Combine(_outputDir, "index.html"), html);
    }

    private string GetExcerpt(string htmlContent)
    {
        // Strip HTML tags
        var text = Regex.Replace(htmlContent, "<.*?>", "");
        
        // Get first 200 characters
        if (text.Length > 200)
        {
            text = text.Substring(0, 200) + "...";
        }
        
        return text;
    }

    private void GenerateRssFeed(List<BlogPost> posts)
    {
        var rss = new StringBuilder();
        rss.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        rss.AppendLine("<rss version=\"2.0\">");
        rss.AppendLine("  <channel>");
        rss.AppendLine("    <title>Vibeblogging</title>");
        rss.AppendLine("    <link>https://csmacnzblog.github.io/Vibeblogging/</link>");
        rss.AppendLine("    <description>A vibe blog</description>");
        rss.AppendLine("    <language>en-us</language>");
        
        foreach (var post in posts.Take(10))
        {
            rss.AppendLine("    <item>");
            rss.AppendLine($"      <title>{EscapeXml(post.Title)}</title>");
            rss.AppendLine($"      <link>https://csmacnzblog.github.io/Vibeblogging/{post.Slug}.html</link>");
            rss.AppendLine($"      <guid>https://csmacnzblog.github.io/Vibeblogging/{post.Slug}.html</guid>");
            rss.AppendLine($"      <pubDate>{post.Date:R}</pubDate>");
            rss.AppendLine($"      <description>{EscapeXml(GetExcerpt(post.Content))}</description>");
            rss.AppendLine("    </item>");
        }
        
        rss.AppendLine("  </channel>");
        rss.AppendLine("</rss>");
        
        File.WriteAllText(Path.Combine(_outputDir, "rss.xml"), rss.ToString());
    }

    private string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private string EscapeHtmlAttribute(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;");
    }

    private void CopyStyles()
    {
        var sourceCss = Path.Join(_templatesDir, "styles.css");
        var destCss = Path.Join(_outputDir, "styles.css");
        File.Copy(sourceCss, destCss, true);
        
        // Copy template images folder
        var sourceImagesDir = Path.Join(_templatesDir, "images");
        var destImagesDir = Path.Join(_outputDir, "images");
        
        if (Directory.Exists(sourceImagesDir))
        {
            if (!Directory.Exists(destImagesDir))
            {
                Directory.CreateDirectory(destImagesDir);
            }
            
            foreach (var file in Directory.GetFiles(sourceImagesDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Join(destImagesDir, fileName);
                File.Copy(file, destFile, true);
            }
        }
        
        // Copy post images folder
        var sourcePostImagesDir = Path.Join(_postsDir, "images");
        var destPostImagesDir = Path.Join(_outputDir, "images", "posts");
        
        if (Directory.Exists(sourcePostImagesDir))
        {
            if (!Directory.Exists(destPostImagesDir))
            {
                Directory.CreateDirectory(destPostImagesDir);
            }
            
            foreach (var file in Directory.GetFiles(sourcePostImagesDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Join(destPostImagesDir, fileName);
                File.Copy(file, destFile, true);
            }
        }
    }
}

public class BlogPost
{
    public string FileName { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Date { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public string Content { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? FeaturedImage { get; set; }
}
