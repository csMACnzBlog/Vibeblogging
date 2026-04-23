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
    private const string SiteBaseUrl = "https://blog.csmac.nz/Vibeblogging";
    private const string SiteDescription = "Vibeblogging - A daily experiment in AI-generated writing, exploring GitHub's agentic workflows and automation capabilities one post at a time.";

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
        
        // Generate paginated index pages
        GenerateIndexPages(posts);
        
        // Generate RSS feed
        GenerateRssFeed(posts);
        
        // Generate sitemap
        GenerateSitemap(posts);
        
        // Generate robots.txt
        GenerateRobotsTxt();
        
        // Generate search page
        GenerateSearchPage(posts);
        
        // Copy CSS and JS assets
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
        post.Content = SanitizeIds(Markdown.ToHtml(content, _pipeline));
        
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
                value = value.Trim('"');
                
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
                .Replace("{{TITLE}}", HtmlEncodeText(post.Title))
                .Replace("{{DATE}}", post.Date.ToString("yyyy-MM-dd"))
                .Replace("{{FORMATTED_DATE}}", post.Date.ToString("MMMM dd, yyyy"))
                .Replace("{{CONTENT}}", post.Content)
                .Replace("{{SEO_META}}", BuildPostSeoMeta(post));
            
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
                html = Regex.Replace(html, @"\{\{#TAGS\}\}.*?\{\{/TAGS\}\}", 
                    $"<span class=\"tags\">{BuildTagButtonsHtml(post.Tags)}</span>", RegexOptions.Singleline);
            }
            else
            {
                html = Regex.Replace(html, @"\{\{#TAGS\}\}.*?\{\{/TAGS\}\}", "", RegexOptions.Singleline);
            }
            
            var outputPath = Path.GetFullPath(Path.Combine(_outputDir, $"{post.Slug}.html"));
            var outputDirNormalized = Path.GetFullPath(_outputDir) + Path.DirectorySeparatorChar;
            if (!outputPath.StartsWith(outputDirNormalized, StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid post slug: path escapes the output directory.");
            File.WriteAllText(outputPath, html);
        }
    }

    private void GenerateIndexPages(List<BlogPost> posts)
    {
        const int pageSize = 15;
        var template = File.ReadAllText(Path.Combine(_templatesDir, "index.html"));

        var totalPages = Math.Max(1, (int)Math.Ceiling((double)posts.Count / pageSize));

        for (int pageNumber = 1; pageNumber <= totalPages; pageNumber++)
        {
            var pagePosts = posts.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
            var postList = BuildPostListHtml(pagePosts);
            var pagination = BuildPaginationHtml(pageNumber, totalPages);
            var pageIndicator = BuildPageIndicatorHtml(pageNumber, totalPages);

            var html = template
                .Replace("{{POST_LIST}}", postList)
                .Replace("{{PAGINATION}}", pagination)
                .Replace("{{PAGE_INDICATOR}}", pageIndicator)
                .Replace("{{SEO_META}}", BuildIndexSeoMeta(pageNumber));

            var fileName = pageNumber == 1 ? "index.html" : $"page{pageNumber}.html";
            File.WriteAllText(Path.Combine(_outputDir, fileName), html);
        }
    }

    private string BuildPostListHtml(List<BlogPost> posts)
    {
        var postList = new StringBuilder();
        foreach (var post in posts)
        {
            var excerpt = GetExcerpt(post.Content);
            postList.AppendLine("<div class=\"post-item\">");

            if (!string.IsNullOrEmpty(post.FeaturedImage))
            {
                postList.AppendLine($"  <img src=\"images/posts/{post.FeaturedImage}\" alt=\"{EscapeHtmlAttribute(post.Title)}\" class=\"post-thumbnail\">");
            }

            postList.AppendLine("  <div class=\"post-item-content\">");
            postList.AppendLine($"    <h2><a href=\"{post.Slug}.html\">{HtmlEncodeText(post.Title)}</a></h2>");
            postList.AppendLine($"    <div class=\"post-meta\">");
            postList.AppendLine($"      <time datetime=\"{post.Date:yyyy-MM-dd}\">{post.Date:MMMM dd, yyyy}</time>");
            if (post.Tags.Any())
            {
                postList.AppendLine($"      <span class=\"tags\">{BuildTagButtonsHtml(post.Tags)}</span>");
            }
            postList.AppendLine($"    </div>");
            postList.AppendLine($"    <div class=\"post-excerpt\">{excerpt}</div>");
            postList.AppendLine("  </div>");
            postList.AppendLine("</div>");
        }
        return postList.ToString();
    }

    private string BuildPaginationHtml(int currentPage, int totalPages)
    {
        if (totalPages <= 1)
            return "";

        var pagination = new StringBuilder();
        pagination.AppendLine("<nav class=\"pagination\" aria-label=\"Page navigation\">");

        if (currentPage > 1)
        {
            var prevFile = currentPage == 2 ? "index.html" : $"page{currentPage - 1}.html";
            pagination.AppendLine($"  <a href=\"{prevFile}\" class=\"pagination-prev\">&#8592; Newer Posts</a>");
        }

        pagination.AppendLine("  <span class=\"pagination-pages\">");
        for (int i = 1; i <= totalPages; i++)
        {
            if (i == currentPage)
            {
                pagination.AppendLine($"    <span class=\"pagination-current\" aria-current=\"page\">{i}</span>");
            }
            else
            {
                var pageFile = i == 1 ? "index.html" : $"page{i}.html";
                pagination.AppendLine($"    <a href=\"{pageFile}\" class=\"pagination-page\">{i}</a>");
            }
        }
        pagination.AppendLine("  </span>");

        if (currentPage < totalPages)
        {
            var nextFile = $"page{currentPage + 1}.html";
            pagination.AppendLine($"  <a href=\"{nextFile}\" class=\"pagination-next\">Older Posts &#8594;</a>");
        }

        pagination.AppendLine("</nav>");
        return pagination.ToString();
    }

    private string BuildPageIndicatorHtml(int currentPage, int totalPages)
    {
        if (currentPage <= 1 || totalPages <= 1)
            return "";

        return $"<p class=\"page-indicator\">Page {currentPage} of {totalPages}</p>\n";
    }

    private string GetExcerpt(string htmlContent)
    {
        var text = Regex.Replace(htmlContent, "<.*?>", "");
        if (text.Length > 200)
            text = text.Substring(0, 200) + "...";
        return text;
    }

    private string GetDescription(string htmlContent)
    {
        var text = Regex.Replace(htmlContent, "<.*?>", "");
        text = text
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > 160)
            text = text.Substring(0, 160) + "...";
        return text;
    }

    private string BuildPostSeoMeta(BlogPost post)
    {
        var description = GetDescription(post.Content);
        var canonicalUrl = $"{SiteBaseUrl}/{post.Slug}.html";
        var ogImage = !string.IsNullOrEmpty(post.FeaturedImage)
            ? $"{SiteBaseUrl}/images/posts/{post.FeaturedImage}"
            : $"{SiteBaseUrl}/images/headerbanner_dark.png";
        var titleAttr = EscapeHtmlAttribute(post.Title + " - Vibeblogging");
        var descAttr = EscapeHtmlAttribute(description);

        var sb = new StringBuilder();
        sb.AppendLine($"<meta name=\"description\" content=\"{descAttr}\">");
        sb.AppendLine($"    <link rel=\"canonical\" href=\"{canonicalUrl}\">");
        sb.AppendLine($"    <meta property=\"og:type\" content=\"article\">");
        sb.AppendLine($"    <meta property=\"og:title\" content=\"{titleAttr}\">");
        sb.AppendLine($"    <meta property=\"og:description\" content=\"{descAttr}\">");
        sb.AppendLine($"    <meta property=\"og:url\" content=\"{canonicalUrl}\">");
        sb.AppendLine($"    <meta property=\"og:image\" content=\"{ogImage}\">");
        sb.AppendLine($"    <meta property=\"og:site_name\" content=\"Vibeblogging\">");
        sb.AppendLine($"    <meta property=\"article:published_time\" content=\"{post.Date:yyyy-MM-ddTHH:mm:ssZ}\">");
        foreach (var tag in post.Tags)
            sb.AppendLine($"    <meta property=\"article:tag\" content=\"{EscapeHtmlAttribute(tag)}\">");
        sb.AppendLine($"    <meta name=\"twitter:card\" content=\"summary_large_image\">");
        sb.AppendLine($"    <meta name=\"twitter:title\" content=\"{titleAttr}\">");
        sb.AppendLine($"    <meta name=\"twitter:description\" content=\"{descAttr}\">");
        sb.AppendLine($"    <meta name=\"twitter:image\" content=\"{ogImage}\">");
        sb.Append(BuildPostStructuredData(post, description, canonicalUrl, ogImage));
        return sb.ToString();
    }

    private string BuildIndexSeoMeta(int pageNumber)
    {
        var canonicalUrl = pageNumber == 1
            ? $"{SiteBaseUrl}/"
            : $"{SiteBaseUrl}/page{pageNumber}.html";
        var ogImage = $"{SiteBaseUrl}/images/headerbanner_dark.png";
        var descAttr = EscapeHtmlAttribute(SiteDescription);
        var titleAttr = EscapeHtmlAttribute("Vibeblogging - A Vibe Blog");

        var sb = new StringBuilder();
        sb.AppendLine($"<meta name=\"description\" content=\"{descAttr}\">");
        sb.AppendLine($"    <link rel=\"canonical\" href=\"{canonicalUrl}\">");
        sb.AppendLine($"    <meta property=\"og:type\" content=\"website\">");
        sb.AppendLine($"    <meta property=\"og:title\" content=\"{titleAttr}\">");
        sb.AppendLine($"    <meta property=\"og:description\" content=\"{descAttr}\">");
        sb.AppendLine($"    <meta property=\"og:url\" content=\"{canonicalUrl}\">");
        sb.AppendLine($"    <meta property=\"og:image\" content=\"{ogImage}\">");
        sb.AppendLine($"    <meta property=\"og:site_name\" content=\"Vibeblogging\">");
        sb.AppendLine($"    <meta name=\"twitter:card\" content=\"summary_large_image\">");
        sb.AppendLine($"    <meta name=\"twitter:title\" content=\"{titleAttr}\">");
        sb.AppendLine($"    <meta name=\"twitter:description\" content=\"{descAttr}\">");
        sb.AppendLine($"    <meta name=\"twitter:image\" content=\"{ogImage}\">");
        sb.Append(BuildIndexStructuredData());
        return sb.ToString();
    }

    private string BuildPostStructuredData(BlogPost post, string description, string canonicalUrl, string ogImage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <script type=\"application/ld+json\">");
        sb.AppendLine("    {");
        sb.AppendLine("      \"@context\": \"https://schema.org\",");
        sb.AppendLine("      \"@type\": \"BlogPosting\",");
        sb.AppendLine($"      \"headline\": {JsonEscapeString(post.Title)},");
        sb.AppendLine($"      \"datePublished\": \"{post.Date:yyyy-MM-ddTHH:mm:ssZ}\",");
        sb.AppendLine($"      \"description\": {JsonEscapeString(description)},");
        sb.AppendLine($"      \"image\": \"{ogImage}\",");
        sb.AppendLine($"      \"url\": \"{canonicalUrl}\",");
        if (post.Tags.Any())
            sb.AppendLine($"      \"keywords\": {JsonEscapeString(string.Join(", ", post.Tags))},");
        sb.AppendLine("      \"author\": {");
        sb.AppendLine("        \"@type\": \"Person\",");
        sb.AppendLine("        \"name\": \"csMACnz\"");
        sb.AppendLine("      },");
        sb.AppendLine("      \"publisher\": {");
        sb.AppendLine("        \"@type\": \"Organization\",");
        sb.AppendLine("        \"name\": \"Vibeblogging\",");
        sb.AppendLine($"        \"url\": \"{SiteBaseUrl}/\"");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.Append("    </script>");
        return sb.ToString();
    }

    private string BuildIndexStructuredData()
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <script type=\"application/ld+json\">");
        sb.AppendLine("    {");
        sb.AppendLine("      \"@context\": \"https://schema.org\",");
        sb.AppendLine("      \"@type\": \"Blog\",");
        sb.AppendLine("      \"name\": \"Vibeblogging\",");
        sb.AppendLine($"      \"url\": \"{SiteBaseUrl}/\",");
        sb.AppendLine($"      \"description\": {JsonEscapeString(SiteDescription)},");
        sb.AppendLine("      \"author\": {");
        sb.AppendLine("        \"@type\": \"Person\",");
        sb.AppendLine("        \"name\": \"csMACnz\"");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.Append("    </script>");
        return sb.ToString();
    }

    private void GenerateSitemap(List<BlogPost> posts)
    {
        var sitemap = new StringBuilder();
        sitemap.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sitemap.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        // Index page
        sitemap.AppendLine("  <url>");
        sitemap.AppendLine($"    <loc>{SiteBaseUrl}/</loc>");
        var newestDate = posts.FirstOrDefault()?.Date;
        if (newestDate.HasValue)
            sitemap.AppendLine($"    <lastmod>{newestDate.Value:yyyy-MM-dd}</lastmod>");
        sitemap.AppendLine("    <changefreq>daily</changefreq>");
        sitemap.AppendLine("    <priority>1.0</priority>");
        sitemap.AppendLine("  </url>");

        // Individual post pages
        foreach (var post in posts)
        {
            sitemap.AppendLine("  <url>");
            sitemap.AppendLine($"    <loc>{SiteBaseUrl}/{post.Slug}.html</loc>");
            sitemap.AppendLine($"    <lastmod>{post.Date:yyyy-MM-dd}</lastmod>");
            sitemap.AppendLine("    <changefreq>monthly</changefreq>");
            sitemap.AppendLine("    <priority>0.8</priority>");
            sitemap.AppendLine("  </url>");
        }

        sitemap.AppendLine("</urlset>");
        File.WriteAllText(Path.Join(_outputDir, "sitemap.xml"), sitemap.ToString());
    }

    private void GenerateRobotsTxt()
    {
        var robots = new StringBuilder();
        robots.AppendLine("User-agent: *");
        robots.AppendLine("Allow: /");
        robots.AppendLine($"Sitemap: {SiteBaseUrl}/sitemap.xml");
        File.WriteAllText(Path.Join(_outputDir, "robots.txt"), robots.ToString());
    }

    private void GenerateRssFeed(List<BlogPost> posts)
    {
        var rss = new StringBuilder();
        rss.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        rss.AppendLine("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\" xmlns:content=\"http://purl.org/rss/1.0/modules/content/\">");
        rss.AppendLine("  <channel>");
        rss.AppendLine("    <title>Vibeblogging</title>");
        rss.AppendLine($"    <link>{SiteBaseUrl}/</link>");
        rss.AppendLine($"    <description>{EscapeXml(SiteDescription)}</description>");
        rss.AppendLine("    <language>en-us</language>");
        rss.AppendLine($"    <atom:link href=\"{SiteBaseUrl}/rss.xml\" rel=\"self\" type=\"application/rss+xml\"/>");
        rss.AppendLine("    <image>");
        rss.AppendLine($"      <url>{SiteBaseUrl}/images/headerbanner_dark.png</url>");
        rss.AppendLine("      <title>Vibeblogging</title>");
        rss.AppendLine($"      <link>{SiteBaseUrl}/</link>");
        rss.AppendLine("    </image>");
        
        foreach (var post in posts)
        {
            rss.AppendLine("    <item>");
            rss.AppendLine($"      <title>{EscapeXml(post.Title)}</title>");
            rss.AppendLine($"      <link>{SiteBaseUrl}/{post.Slug}.html</link>");
            rss.AppendLine($"      <guid>{SiteBaseUrl}/{post.Slug}.html</guid>");
            rss.AppendLine($"      <pubDate>{post.Date:R}</pubDate>");
            rss.AppendLine($"      <description>{EscapeXml(GetDescription(post.Content))}</description>");
            rss.AppendLine($"      <content:encoded><![CDATA[{post.Content}]]></content:encoded>");
            rss.AppendLine("    </item>");
        }
        
        rss.AppendLine("  </channel>");
        rss.AppendLine("</rss>");
        
        File.WriteAllText(Path.Combine(_outputDir, "rss.xml"), rss.ToString());
    }

    private static readonly Regex _idAttributeRegex = new(@"id=""([^""]*)""", RegexOptions.Compiled);

    private string SanitizeIds(string html)
    {
        return _idAttributeRegex.Replace(html, m =>
        {
            var id = m.Groups[1].Value.Replace(".", "");
            return $"id=\"{id}\"";
        });
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

    private string HtmlEncodeText(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private string BuildTagButtonsHtml(List<string> tags)
    {
        return string.Join(", ", tags.Select(t =>
            $"<button type=\"button\" class=\"tag-link\" data-tag=\"{EscapeHtmlAttribute(t)}\">{EscapeXml(t)}</button>"));
    }

    private void GenerateSearchPage(List<BlogPost> posts)
    {
        var templatePath = Path.Combine(_templatesDir, "search.html");
        if (!File.Exists(templatePath))
            return;

        var template = File.ReadAllText(templatePath);
        var postsJson = BuildPostsJson(posts);
        var html = template.Replace("{{POSTS_JSON}}", postsJson);
        File.WriteAllText(Path.Combine(_outputDir, "search.html"), html);
    }

    private string BuildPostsJson(List<BlogPost> posts)
    {
        var sb = new StringBuilder();
        sb.Append("[");
        for (int i = 0; i < posts.Count; i++)
        {
            var post = posts[i];
            var excerpt = GetExcerpt(post.Content);
            if (i > 0) sb.Append(",");
            sb.AppendLine();
            sb.Append("  {");
            sb.Append($"\"title\":{JsonEscapeString(post.Title)},");
            sb.Append($"\"slug\":{JsonEscapeString(post.Slug)},");
            sb.Append($"\"date\":{JsonEscapeString(post.Date.ToString("yyyy-MM-dd"))},");
            sb.Append($"\"formattedDate\":{JsonEscapeString(post.Date.ToString("MMMM dd, yyyy"))},");
            sb.Append($"\"tags\":[{string.Join(",", post.Tags.Select(t => JsonEscapeString(t)))}],");
            sb.Append($"\"excerpt\":{JsonEscapeString(excerpt)},");
            sb.Append($"\"image\":{(string.IsNullOrEmpty(post.FeaturedImage) ? "null" : JsonEscapeString(post.FeaturedImage))}");
            sb.Append("}");
        }
        sb.AppendLine();
        sb.Append("]");
        return sb.ToString();
    }

    private string JsonEscapeString(string text)
    {
        return "\"" + text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            + "\"";
    }

    private void CopyStyles()
    {
        var sourceCss = Path.Join(_templatesDir, "styles.css");
        var destCss = Path.Join(_outputDir, "styles.css");
        File.Copy(sourceCss, destCss, true);

        // Copy search modal JS
        var sourceModalJs = Path.Join(_templatesDir, "search-modal.js");
        var destModalJs = Path.Join(_outputDir, "search-modal.js");
        if (File.Exists(sourceModalJs))
        {
            File.Copy(sourceModalJs, destModalJs, true);
        }
        
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
