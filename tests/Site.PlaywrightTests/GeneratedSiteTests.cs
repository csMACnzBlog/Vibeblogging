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
        
        // Check that the page title contains "Vibeblogging"
        await Assertions.Expect(_page).ToHaveTitleAsync(new Regex("Vibeblogging"));
        
        // Check that the header area with background image is present
        var headerArea = _page.Locator(".header-area");
        await Assertions.Expect(headerArea).ToBeVisibleAsync();
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

    [Fact]
    public async Task SearchModal_ShouldOpenOnSearchButtonClick()
    {
        await _page.GotoAsync(_fixture.ServerUrl);

        // Search modal should be hidden initially
        var modal = _page.Locator("#search-modal");
        await Assertions.Expect(modal).ToBeHiddenAsync();

        // Click the search button
        var searchBtn = _page.Locator("#search-btn");
        await searchBtn.ClickAsync();

        // Modal should be visible
        await Assertions.Expect(modal).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SearchModal_SearchButtonShouldBeAlignedWithMenuItems()
    {
        await _page.GotoAsync(_fixture.ServerUrl);

        // Get bounding boxes of the last link menu item and the search button
        var lastLink = _page.Locator(".header-bar .menu-item-content").Nth(4); // VibeBlog link
        var searchBtn = _page.Locator("#search-btn");

        var linkBox = await lastLink.BoundingBoxAsync();
        var btnBox = await searchBtn.BoundingBoxAsync();

        Assert.NotNull(linkBox);
        Assert.NotNull(btnBox);

        // Heights should match
        Assert.Equal(linkBox.Height, btnBox.Height, 1.0);

        // Vertical centers should match within 2px
        var linkCenterY = linkBox.Y + linkBox.Height / 2;
        var btnCenterY = btnBox.Y + btnBox.Height / 2;
        Assert.True(Math.Abs(linkCenterY - btnCenterY) <= 2,
            $"Search button center Y ({btnCenterY:F1}) should align with menu link center Y ({linkCenterY:F1})");
    }

    [Fact]
    public async Task SearchPage_TagsShouldStartHidden()
    {
        await _page.GotoAsync($"{_fixture.ServerUrl}/search.html");

        // Wait for the page to load
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Tag buttons should all be hidden initially
        var tagButtons = _page.Locator(".tag-btn");
        var count = await tagButtons.CountAsync();
        Assert.True(count > 0, "There should be tag buttons");

        // All tag buttons should be hidden
        for (int i = 0; i < count; i++)
        {
            await Assertions.Expect(tagButtons.Nth(i)).ToBeHiddenAsync();
        }

        // The toggle button should say "Show tags"
        var toggleBtn = _page.Locator("#tag-toggle-btn");
        await Assertions.Expect(toggleBtn).ToBeVisibleAsync();
        await Assertions.Expect(toggleBtn).ToHaveTextAsync("Show tags");
    }

    [Fact]
    public async Task SearchPage_TagsShouldExpandOnToggle()
    {
        await _page.GotoAsync($"{_fixture.ServerUrl}/search.html");
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var toggleBtn = _page.Locator("#tag-toggle-btn");
        
        // Click to expand
        await toggleBtn.ClickAsync();

        // Toggle button should now say "Hide tags"
        await Assertions.Expect(toggleBtn).ToHaveTextAsync("Hide tags");

        // At least one tag button should be visible
        var tagButtons = _page.Locator(".tag-btn");
        await Assertions.Expect(tagButtons.First).ToBeVisibleAsync();

        // Click to collapse
        await toggleBtn.ClickAsync();

        // Toggle button should say "Show tags" again
        await Assertions.Expect(toggleBtn).ToHaveTextAsync("Show tags");

        // All tag buttons should be hidden again (none active)
        var count = await tagButtons.CountAsync();
        for (int i = 0; i < count; i++)
        {
            await Assertions.Expect(tagButtons.Nth(i)).ToBeHiddenAsync();
        }
    }

    [Fact]
    public async Task SearchPage_ActiveTagsShouldRemainVisibleWhenCollapsed()
    {
        await _page.GotoAsync($"{_fixture.ServerUrl}/search.html");
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var toggleBtn = _page.Locator("#tag-toggle-btn");

        // Expand tags
        await toggleBtn.ClickAsync();

        // Click the first tag to activate it
        var firstTag = _page.Locator(".tag-btn").First;
        var tagName = await firstTag.TextContentAsync();
        await firstTag.ClickAsync();

        // Verify the tag is active
        await Assertions.Expect(firstTag).ToHaveClassAsync(new Regex("active"));

        // Collapse tags
        await toggleBtn.ClickAsync();
        await Assertions.Expect(toggleBtn).ToHaveTextAsync("Show tags");

        // The active tag should still be visible
        await Assertions.Expect(firstTag).ToBeVisibleAsync();

        // Other tag buttons should be hidden
        var allTagButtons = _page.Locator(".tag-btn");
        var count = await allTagButtons.CountAsync();
        int visibleCount = 0;
        for (int i = 0; i < count; i++)
        {
            var isVisible = await allTagButtons.Nth(i).IsVisibleAsync();
            if (isVisible) visibleCount++;
        }
        Assert.Equal(1, visibleCount); // Only the active tag visible
    }

    [Fact]
    public async Task SearchPage_TagsShouldBeOrderedByUsageFrequency()
    {
        await _page.GotoAsync($"{_fixture.ServerUrl}/search.html");
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var toggleBtn = _page.Locator("#tag-toggle-btn");

        // Expand tags to see them all
        await toggleBtn.ClickAsync();

        // The first tag should be a frequently-used one (csharp appears in most posts)
        var firstTag = _page.Locator(".tag-btn").First;
        var firstName = await firstTag.TextContentAsync();

        // csharp is the most common tag across posts
        Assert.Equal("csharp", firstName);
    }

    [Fact]
    public async Task SearchPage_FocusedActiveTagShouldHaveInsetBoxShadow()
    {
        await _page.GotoAsync($"{_fixture.ServerUrl}/search.html");
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var toggleBtn = _page.Locator("#tag-toggle-btn");

        // Expand tags
        await toggleBtn.ClickAsync();

        // Activate the first tag
        var firstTag = _page.Locator(".tag-btn").First;
        await firstTag.ClickAsync();
        await Assertions.Expect(firstTag).ToHaveClassAsync(new Regex("active"));

        // Focus the active tag
        await firstTag.FocusAsync();

        // The box-shadow should contain "inset" (white inset ring applied to active+focused)
        var boxShadow = await firstTag.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
        Assert.Contains("inset", boxShadow);
    }
}
