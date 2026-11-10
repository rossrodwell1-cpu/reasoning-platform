using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

internal static class PwaChecks
{
    public static async Task Run(IBrowser browser, string url)
    {
        // A standard Playwright context is incognito, where Chromium disables
        // installation. Use an isolated, non-private profile for eligibility.
        var profile = Path.Combine(Path.GetTempPath(), "reasoninglab-pwa-" + Guid.NewGuid().ToString("N"));
        await using var context = await browser.BrowserType.LaunchPersistentContextAsync(profile, new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("BROWSER_EXECUTABLE")
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        await page.GotoAsync(url);
        await Expect(page.GetByLabel("Theme", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("link[rel=manifest]")).ToHaveAttributeAsync("href", "manifest.webmanifest");

        var response = await context.APIRequest.GetAsync(url + "/manifest.webmanifest");
        if (!response.Ok || !response.Headers["content-type"].Contains("manifest+json"))
            throw new Exception("Manifest must be served successfully with its manifest media type.");
        using var manifestDocument = JsonDocument.Parse(await response.TextAsync());
        var manifest = manifestDocument.RootElement;
        foreach (var (key, value) in new[] { ("id", "/"), ("scope", "/"), ("start_url", "/"), ("display", "standalone"), ("name", "ReasoningLab") })
            if (manifest.GetProperty(key).GetString() != value) throw new Exception($"Unexpected manifest {key}.");
        foreach (var icon in manifest.GetProperty("icons").EnumerateArray())
        {
            var dimensions = await page.EvaluateAsync<string>("""
                async src => {
                    const image = new Image(); image.src = src; await image.decode();
                    return `${image.naturalWidth}x${image.naturalHeight}`;
                }
                """, icon.GetProperty("src").GetString());
            if (dimensions != icon.GetProperty("sizes").GetString()) throw new Exception("Icon dimensions mismatch.");
        }
        await Expect(page.Locator("link[rel=apple-touch-icon]")).ToHaveAttributeAsync("sizes", "180x180");
        if (!await page.EvaluateAsync<bool>("""
            async () => {
                const image = new Image(); image.src = document.querySelector('link[rel="apple-touch-icon"]').href;
                await image.decode(); return image.naturalWidth === 180 && image.naturalHeight === 180;
            }
            """)) throw new Exception("Apple touch icon did not load at 180px.");

        // Eligibility diagnostics are not proof of a physical OS installation.
        var cdp = await context.NewCDPSessionAsync(page);
        var parsed = (await cdp.SendAsync("Page.getAppManifest"))!.Value;
        if (parsed.GetProperty("errors").GetArrayLength() != 0) throw new Exception(parsed.ToString());
        var eligibility = (await cdp.SendAsync("Page.getInstallabilityErrors"))!.Value;
        if (eligibility.GetProperty("installabilityErrors").GetArrayLength() != 0) throw new Exception(eligibility.ToString());
        if (await page.EvaluateAsync<int>("navigator.serviceWorker.getRegistrations().then(r => r.length)") != 0)
            throw new Exception("This milestone must not register a service worker.");
        Console.WriteLine("PASS published manifest, icon sizes, Chromium installation eligibility; no service worker registered.");

        await page.GotoAsync(url + "/practice/general");
        await Expect(page.Locator(".answer-option").First).ToBeVisibleAsync();
        foreach (var (width, height) in new[] { (1440, 1000), (820, 1180), (1180, 820), (390, 844), (844, 390), (320, 700) })
        {
            await page.SetViewportSizeAsync(width, height);
            foreach (var theme in new[] { "light", "dark", "warm" })
            {
                await page.GetByLabel("Theme", new() { Exact = true }).SelectOptionAsync(theme);
                await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", theme);
                await AssertNoOverflow(page, $"{theme}, {width}x{height}");
            }
            // Simulated notch/home-indicator insets verify CSS bounds, not physical hardware.
            await page.EvaluateAsync("""
                () => {
                    for (const [side, value] of Object.entries({ top:24, right:44, bottom:34, left:44 }))
                        document.documentElement.style.setProperty('--safe-' + side, value + 'px');
                }
                """);
            await AssertNoOverflow(page, $"safe areas, {width}x{height}");
            if (!await page.Locator(".topbar").EvaluateAsync<bool>("e => { const r=e.getBoundingClientRect(); return r.left >= 44 && r.right <= innerWidth-44; }"))
                throw new Exception("Header escaped safe-area bounds.");
            if (width <= 760)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Menu", Exact = true }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Content studio", Exact = false })).ToBeVisibleAsync();
                await AssertNoOverflow(page, "expanded mobile navigation");
                await page.GetByRole(AriaRole.Button, new() { Name = "Close menu", Exact = true }).ClickAsync();
            }
            if (width == 844 || width == 390)
                await page.ScreenshotAsync(new() { Path = $"artifacts/pwa-safe-area-{width}.png", FullPage = true });
            await page.EvaluateAsync("['top','right','bottom','left'].forEach(s => document.documentElement.style.removeProperty('--safe-' + s))");
            Console.WriteLine($"PASS PWA layout {width}x{height}: three themes, safe-area simulation and navigation.");
        }
        await page.ReloadAsync();
        await Expect(page.GetByLabel("Theme", new() { Exact = true })).ToHaveValueAsync("warm");
        await Expect(page.Locator(".answer-option").First).ToBeVisibleAsync();
        if (errors.Count != 0) throw new Exception(string.Join(Environment.NewLine, errors));
        Console.WriteLine("PASS direct practice launch, theme persistence; no page/console errors. Physical installation remains a manual check.");
    }

    private static async Task AssertNoOverflow(IPage page, string label)
    {
        if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"))
            throw new Exception($"Horizontal overflow: {label}");
    }
}
