using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Site.PlaywrightTests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class GeneratedSiteTests : PageTest
{
    private string _outputDir = null!;
    private string _serverUrl = null!;
    private Process? _serverProcess;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // Generate the site first
        _outputDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "output");
        
        var generatorDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "src", "SiteGenerator");
        
        // Run the generator
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project {generatorDir}",
            WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(processInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }

        // Start a simple HTTP server to serve the generated site
        _serverUrl = "http://localhost:5555";
        await StartHttpServer();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _serverProcess?.Kill();
        _serverProcess?.Dispose();
    }

    private async Task StartHttpServer()
    {
        // Start a simple HTTP server using Python
        var processInfo = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"-m http.server 5555 --directory {_outputDir}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _serverProcess = Process.Start(processInfo);
        
        // Wait for server to start
        await Task.Delay(2000);
    }

    [Test]
    public async Task IndexPage_ShouldLoad()
    {
        await Page.GotoAsync(_serverUrl);
        
        // Check that the page title is correct
        await Expect(Page).ToHaveTitleAsync(new Regex("Vibeblogging"));
    }

    [Test]
    public async Task IndexPage_ShouldDisplayBlogTitle()
    {
        await Page.GotoAsync(_serverUrl);
        
        // Check that the main heading is present
        var heading = Page.Locator("h1");
        await Expect(heading).ToBeVisibleAsync();
        await Expect(heading).ToContainTextAsync("Vibeblogging");
    }

    [Test]
    public async Task IndexPage_ShouldHaveStylesheet()
    {
        await Page.GotoAsync(_serverUrl);
        
        // Check that the stylesheet is loaded
        var stylesheetLink = Page.Locator("link[rel='stylesheet']");
        await Expect(stylesheetLink).ToHaveAttributeAsync("href", new Regex("styles\\.css"));
    }

    [Test]
    public async Task StylesheetFile_ShouldBeAccessible()
    {
        var response = await Page.GotoAsync($"{_serverUrl}/styles.css");
        
        // Check that the stylesheet returns 200 OK
        Assert.That(response?.Status, Is.EqualTo(200));
    }

    [Test]
    public async Task IndexPage_ShouldListPosts()
    {
        await Page.GotoAsync(_serverUrl);
        
        // Check that there are post items displayed
        var postItems = Page.Locator(".post-item");
        await Expect(postItems.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task PostPage_ShouldLoad()
    {
        await Page.GotoAsync(_serverUrl);
        
        // Find and click the first post link
        var firstPostLink = Page.Locator(".post-item h2 a").First;
        await firstPostLink.ClickAsync();
        
        // Check that we navigated to a post page
        await Expect(Page.Locator("article")).ToBeVisibleAsync();
    }

    [Test]
    public async Task PostPage_ShouldHaveTitle()
    {
        await Page.GotoAsync(_serverUrl);
        
        var firstPostLink = Page.Locator(".post-item h2 a").First;
        var postTitle = await firstPostLink.InnerTextAsync();
        await firstPostLink.ClickAsync();
        
        // Check that the post title is displayed (use First to avoid strict mode violation)
        var articleTitle = Page.Locator("article h1").First;
        await Expect(articleTitle).ToContainTextAsync(postTitle);
    }

    [Test]
    public async Task PostPage_ShouldHaveDate()
    {
        await Page.GotoAsync(_serverUrl);
        
        var firstPostLink = Page.Locator(".post-item h2 a").First;
        await firstPostLink.ClickAsync();
        
        // Check that the post has a date
        var dateElement = Page.Locator("time");
        await Expect(dateElement).ToBeVisibleAsync();
    }

    [Test]
    public async Task RssFeed_ShouldBeAccessible()
    {
        var response = await Page.GotoAsync($"{_serverUrl}/rss.xml");
        
        // Check that the RSS feed returns 200 OK
        Assert.That(response?.Status, Is.EqualTo(200));
        
        // Check that it contains XML
        var content = await response!.TextAsync();
        Assert.That(content, Does.Contain("<?xml"));
        Assert.That(content, Does.Contain("<rss"));
    }

    [Test]
    public async Task RssFeed_ShouldContainPosts()
    {
        await Page.GotoAsync($"{_serverUrl}/rss.xml");
        
        var content = await Page.ContentAsync();
        
        // Check that the RSS feed contains items
        Assert.That(content, Does.Contain("<item>"));
        Assert.That(content, Does.Contain("<title>"));
        Assert.That(content, Does.Contain("<link>"));
    }
}
