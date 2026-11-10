using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

internal static class ThemeChecks
{
    public static async Task Run(IBrowser browser, string url)
    {
        await using var context = await browser.NewContextAsync(new() { ColorScheme = ColorScheme.Dark });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(url);
        var picker = page.GetByLabel("Theme", new() { Exact = true });
        await Expect(picker).ToHaveValueAsync("system");
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");

        foreach (var theme in new[] { "light", "dark", "warm" })
        {
            await picker.SelectOptionAsync(theme);
            await page.ReloadAsync();
            await Expect(picker).ToHaveValueAsync(theme);
            await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", theme);
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
            await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", theme);
            var contrastFailures = await page.EvaluateAsync<string[]>("""
                () => {
                    const css = getComputedStyle(document.documentElement);
                    function luminance(token) {
                        let hex = css.getPropertyValue('--' + token).trim().slice(1);
                        if (hex.length === 3) hex = [...hex].map(c => c + c).join('');
                        const rgb = [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
                            .map(c => c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4);
                        return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722;
                    }
                    return [['text','surface'], ['muted','canvas'], ['muted','surface-subtle'],
                        ['primary','selected'], ['on-primary','primary'], ['success','success-bg'],
                        ['review','review-bg'], ['error','error-bg'], ['control','surface']]
                        .flatMap(([fg, bg]) => {
                            const values = [luminance(fg), luminance(bg)].sort((a,b) => a-b);
                            const ratio = (values[1] + .05) / (values[0] + .05);
                            return ratio >= (fg === 'control' ? 3 : 4.5) ? [] : [`${fg}/${bg}: ${ratio}`];
                        });
                }
                """);
            if (contrastFailures.Length > 0) throw new Exception($"{theme} contrast: {string.Join(", ", contrastFailures)}");
            foreach (var width in new[] { 1440, 820, 390, 320 })
            {
                await page.SetViewportSizeAsync(width, 1000);
                foreach (var path in new[] { "/", "/practice/general", "/practice/lsat", "/practice/ucat", "/skills", "/admin", "/missing-page" })
                {
                    await page.GotoAsync(url + path);
                    await Expect(picker).ToHaveValueAsync(theme);
                    await Expect(page.Locator("main h1")).ToBeVisibleAsync();
                    if (path.StartsWith("/practice/")) await Expect(page.Locator(".answer-option").First).ToBeVisibleAsync();
                    if (path == "/skills") await Expect(page.Locator(".skill-library")).ToBeVisibleAsync();
                    await page.EvaluateAsync("document.fonts.ready");
                    if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"))
                        throw new Exception($"Overflow: {theme}, {width}, {path}");
                    if (width == 390 && path == "/practice/general")
                        await page.ScreenshotAsync(new() { Path = $"artifacts/{theme}-practice-mobile.png", FullPage = true });
                    if (width == 1440 && path == "/")
                        await page.ScreenshotAsync(new() { Path = $"artifacts/{theme}-overview.png", FullPage = true });
                }
            }
            Console.WriteLine($"PASS {theme}: all routes at 1440, 820, 390 and 320px; reload persistence and explicit override.");
        }

        await page.GotoAsync(url + "/practice/general");
        var menu = page.GetByRole(AriaRole.Button, new() { Name = "Menu", Exact = true });
        await menu.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(".menu-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
        await page.GetByRole(AriaRole.Link, new() { Name = "Content studio", Exact = false }).ClickAsync();
        await Expect(page.Locator("main h1")).ToHaveTextAsync("Give a good question a home.");
        await Expect(page.Locator(".menu-toggle")).ToHaveAttributeAsync("aria-expanded", "false");

        await page.GotoAsync(url + "/practice/general");
        var radios = page.GetByRole(AriaRole.Radio);
        await radios.First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(radios.Nth(1)).ToBeCheckedAsync();
        if (await radios.Nth(1).EvaluateAsync<string>("e => getComputedStyle(e).width") != "24px")
            throw new Exception("Selected radio indicator is not the approved size.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false }).ClickAsync();
        await Expect(page.Locator(".answer-tag").First).ToBeVisibleAsync();
        await Expect(page.Locator(".feedback")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = "artifacts/theme-feedback-mobile.png", FullPage = true });

        await page.EmulateMediaAsync(new() { ForcedColors = ForcedColors.Active });
        if (await radios.Nth(1).EvaluateAsync<string>("e => getComputedStyle(e).appearance") != "auto")
            throw new Exception("High contrast mode did not restore native radio appearance.");
        await page.EmulateMediaAsync(new() { ForcedColors = ForcedColors.None });

        await picker.SelectOptionAsync("system");
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce });
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        if (!await page.EvaluateAsync<bool>("localStorage.getItem('reasoninglab.theme') === null"))
            throw new Exception("System preference did not clear explicit selection.");
        if (!await page.EvaluateAsync<bool>("document.fonts.check('650 24px Manrope') && document.fonts.check('400 18px \"Source Sans 3\"')"))
            throw new Exception("Local fonts failed to load.");
        if (await page.Locator(".button").First.EvaluateAsync<string>("e => getComputedStyle(e).transitionDuration") != "0s")
            throw new Exception("Reduced motion not respected.");

        // A 640 CSS pixel viewport exercises the reflow of a 1280px window at 200% zoom.
        await page.SetViewportSizeAsync(640, 900);
        await page.GotoAsync(url + "/admin");
        if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"))
            throw new Exception("Zoom-equivalent reflow failed.");
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync(url + "/practice/general");
        await Expect(page.Locator(".answer-option").First).ToBeVisibleAsync();
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"))
            throw new Exception("200% text enlargement overflow.");
        await page.EvaluateAsync("document.documentElement.style.removeProperty('font-size')");

        // Check startup independently of Blazor, including corrupt and inaccessible storage.
        foreach (var storage in new[] { "warm", "invalid", "blocked" })
        {
            await using var startup = await browser.NewContextAsync(new() { ColorScheme = ColorScheme.Dark });
            await startup.AddInitScriptAsync(storage == "blocked"
                ? "Object.defineProperty(window, 'localStorage', { get() { throw new DOMException('Blocked', 'SecurityError'); } });"
                : $"localStorage.setItem('reasoninglab.theme', '{storage}');");
            await startup.RouteAsync("**/_framework/**", route => route.AbortAsync());
            var initial = await startup.NewPageAsync();
            await initial.GotoAsync(url);
            await Expect(initial.Locator("html")).ToHaveAttributeAsync("data-theme", storage == "warm" ? "warm" : "dark");
            if (storage == "blocked")
            {
                await initial.EvaluateAsync("reasoningTheme.set('light')");
                await Expect(initial.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
            }
        }
        if (errors.Count > 0) throw new Exception(string.Join(Environment.NewLine, errors));
        Console.WriteLine("PASS theme startup, storage fallback, system changes, mobile menu, radio keyboard selection, feedback labels, fonts and reduced motion.");
    }
}
