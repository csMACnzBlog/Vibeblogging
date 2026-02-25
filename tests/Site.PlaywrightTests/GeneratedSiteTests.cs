using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Site.PlaywrightTests;

public class PlaywrightFixture : IAsyncLifetime
{
    public string OutputDir { get; private set; } = null!;
    public string ServerUrl { get; private set; } = null!;
    private Process? _serverProcess;
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // Generate the site first
        OutputDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "output");
        
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
        ServerUrl = "http://localhost:5555";
        await StartHttpServer();

        // Initialize Playwright
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async ValueTask DisposeAsync()
    {
        _serverProcess?.Kill();
        _serverProcess?.Dispose();
        
        if (Browser != null)
            await Browser.CloseAsync();
        
        if (Playwright != null)
            Playwright.Dispose();
    }

    private async Task StartHttpServer()
    {
        // Start a simple HTTP server using Python
        var processInfo = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"-m http.server 5555 --directory {OutputDir}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _serverProcess = Process.Start(processInfo);
        
        // Wait for server to start and verify it's accessible
        var httpClient = new HttpClient();
        for (int i = 0; i < 30; i++)
        {
            try
            {
                await Task.Delay(500);
                var response = await httpClient.GetAsync($"{ServerUrl}/index.html");
                if (response.IsSuccessStatusCode)
                {
                    break;
                }
            }
            catch
            {
                // Server not ready yet
            }
        }
    }
}

[CollectionDefinition("Playwright collection")]
public class PlaywrightCollection : ICollectionFixture<PlaywrightFixture>
{
}

[Collection("Playwright collection")]
public class GeneratedSiteTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public GeneratedSiteTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        _page = await _fixture.Browser.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _page.CloseAsync();
    }

    [Fact]
    public async Task IndexPage_ShouldLoad()
    {
        await _page.GotoAsync(_fixture.ServerUrl);
        
        // Check that the page title is correct
        await Assertions.Expect(_page).ToHaveTitleAsync(new Regex("Vibeblogging"));
    }

    [Fact]
    public async Task IndexPage_ShouldDisplayBlogTitle()
    {
        await _page.GotoAsync(_fixture.ServerUrl);
        
        // Check that the main heading is present
        var heading = _page.Locator("h1");
        await Assertions.Expect(heading).ToBeVisibleAsync();
        await Assertions.Expect(heading).ToContainTextAsync("Vibeblogging");
    }

    [Fact]
    public async Task IndexPage_ShouldHaveStylesheet()
    {
        await _page.GotoAsync(_fixture.ServerUrl);
        
        // Check that the stylesheet is loaded
        var stylesheetLink = _page.Locator("link[rel='stylesheet']");
        await Assertions.Expect(stylesheetLink).ToHaveAttributeAsync("href", new Regex("styles\\.css"));
    }

    [Fact]
    public async Task StylesheetFile_ShouldBeAccessible()
    {
        var response = await _page.GotoAsync($"{_fixture.ServerUrl}/styles.css");
        
        // Check that the stylesheet returns 200 OK
        Assert.Equal(200, response?.Status);
    }

    [Fact]
    public async Task IndexPage_ShouldListPosts()
    {
        await _page.GotoAsync(_fixture.ServerUrl);
        
        // Check that there are post items displayed
        var postItems = _page.Locator(".post-item");
        await Assertions.Expect(postItems.First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PostPage_ShouldLoad()
    {
        await _page.GotoAsync(_fixture.ServerUrl);
        
        // Find and click the first post link
        var firstPostLink = _page.Locator(".post-item h2 a").First;
        await firstPostLink.ClickAsync();
        
        // Check that we navigated to a post page
        await Assertions.Expect(_page.Locator("article")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PostPage_ShouldHaveTitle()
    {
        await _page.GotoAsync(_fixture.ServerUrl);
        
        var firstPostLink = _page.Locator(".post-item h2 a").First;
        var postTitle = await firstPostLink.InnerTextAsync();
        await firstPostLink.ClickAsync();
        
        // Check that the post title is displayed (use First to avoid strict mode violation)
        var articleTitle = _page.Locator("article h1").First;
        await Assertions.Expect(articleTitle).ToContainTextAsync(postTitle);
    }

    [Fact]
    public async Task PostPage_ShouldHaveDate()
    {
        await _page.GotoAsync(_fixture.ServerUrl);
        
        var firstPostLink = _page.Locator(".post-item h2 a").First;
        await firstPostLink.ClickAsync();
        
        // Check that the post has a date
        var dateElement = _page.Locator("time");
        await Assertions.Expect(dateElement).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RssFeed_ShouldBeAccessible()
    {
        var response = await _page.GotoAsync($"{_fixture.ServerUrl}/rss.xml");
        
        // Check that the RSS feed returns 200 OK
        Assert.Equal(200, response?.Status);
        
        // Check that it contains XML
        var content = await response!.TextAsync();
        Assert.Contains("<?xml", content);
        Assert.Contains("<rss", content);
    }

    [Fact]
    public async Task RssFeed_ShouldContainPosts()
    {
        await _page.GotoAsync($"{_fixture.ServerUrl}/rss.xml");
        
        var content = await _page.ContentAsync();
        
        // Check that the RSS feed contains items
        Assert.Contains("<item>", content);
        Assert.Contains("<title>", content);
        Assert.Contains("<link>", content);
    }
}
