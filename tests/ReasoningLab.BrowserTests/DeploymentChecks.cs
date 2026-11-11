using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

internal static class DeploymentChecks
{
    private static readonly string[] Assets = ["/", "/manifest.webmanifest", "/css/app.css", "/js/theme.js", "/js/startup.js",
        "/fonts/manrope.woff2", "/icons/icon-192.png", "/_framework/blazor.webassembly.js",
        "/_framework/dotnet.js", "/_framework/ReasoningLab.Client.wasm", "/_framework/ReasoningLab.Core.wasm"];

    public static async Task Run(IBrowser browser)
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("UPDATE_RELEASE_ROOT") ?? "artifacts/update-releases");
        var url = Environment.GetEnvironmentVariable("UPDATE_TEST_URL") ?? "http://localhost:5197";
        var keys = Path.GetFullPath("artifacts/update-keys");
        var profile = Path.GetFullPath("artifacts/update-browser-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "A");
        var b = Path.Combine(root, "B");
        if (HashFile(a, "/_framework/ReasoningLab.Client.wasm") == HashFile(b, "/_framework/ReasoningLab.Client.wasm"))
            throw new Exception("A/B fixtures must contain different compiled client assemblies.");
        await using var context = await browser.BrowserType.LaunchPersistentContextAsync(profile, new()
        {
            Headless = true, ExecutablePath = Environment.GetEnvironmentVariable("BROWSER_EXECUTABLE")
        });
        var originalProcessId = await BrowserProcessId(context);
        var errors = new List<string>();
        var practice = await context.NewPageAsync();
        practice.PageError += (_, error) => errors.Add(error);
        practice.Console += (_, message) => { if (message.Type == "error") Console.WriteLine(message.Text); };
        var diagnostic = await context.NewPageAsync();
        diagnostic.PageError += (_, error) => errors.Add(error);
        PublishedServer? server = null;
        var restartRetainedIdentity = false;
        try
        {
            server = await PublishedServer.Start(a, url, keys, "A");
            await practice.GotoAsync(url + "/practice/general");
            await Expect(practice.GetByRole(AriaRole.Radio).First).ToBeEnabledAsync();
            await practice.GetByLabel("Theme", new() { Exact = true }).SelectOptionAsync("warm");
            await practice.GetByRole(AriaRole.Radio).First.CheckAsync();
            var answer = await practice.RunAndWaitForResponseAsync(
                () => practice.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false }).ClickAsync(),
                response => response.Url.EndsWith("/answers"));
            await NoStore(answer);
            await Expect(practice.GetByText("That's sound reasoning.", new() { Exact = true })).ToBeVisibleAsync();
            await practice.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = false }).ClickAsync();
            await practice.GetByRole(AriaRole.Radio, new() { Name = "Noor owns a helmet." }).CheckAsync();
            var prompt = await practice.Locator("#question-title").InnerTextAsync();
            var cookie = (await context.CookiesAsync()).Single(c => c.Name == "reasoning-profile").Value;
            var dashboardA = await ApiGet(context, url + "/api/dashboard");
            if (dashboardA.GetProperty("skills")[0].GetProperty("attempts").GetInt32() != 1)
                throw new Exception("Expected durable progress before the update.");
            await diagnostic.GotoAsync(url + "/practice/lsat");
            await Expect(diagnostic.GetByRole(AriaRole.Radio).First).ToBeEnabledAsync();
            var started = await diagnostic.RunAndWaitForResponseAsync(
                () => diagnostic.GetByRole(AriaRole.Button, new() { Name = "Start short diagnostic", Exact = false }).ClickAsync(),
                response => response.Url.EndsWith("/api/sessions") && response.Request.Method == "POST");
            await NoStore(started);
            using var diagnosticStart = JsonDocument.Parse(await started.TextAsync());
            var diagnosticId = diagnosticStart.RootElement.GetProperty("id").GetString();
            // Compare persisted values on both sides of deployment; PostgreSQL
            // timestamps have microsecond rather than .NET tick precision.
            var persistedDiagnostic = await ApiGet(context, $"{url}/api/sessions/{diagnosticId}");
            var deadline = persistedDiagnostic.GetProperty("deadline").GetString();
            await Expect(diagnostic.GetByText("Mara has completed an inspection.", new() { Exact = true })).ToBeVisibleAsync();
            await diagnostic.GetByRole(AriaRole.Radio).First.CheckAsync();
            await practice.EvaluateAsync("window.updateSentinel = 'active-A'");
            await diagnostic.EvaluateAsync("window.updateSentinel = 'active-A'");
            var navigations = 0;
            practice.FrameNavigated += (_, frame) => { if (frame == practice.MainFrame) navigations++; };
            diagnostic.FrameNavigated += (_, frame) => { if (frame == diagnostic.MainFrame) navigations++; };

            var validators = await CheckHeaders(context, url);
            await CheckBrowserAssets(practice, a);
            await CheckCaches(practice);
            Console.WriteLine("PASS release A: revalidation headers, validators, compiled assets, no-store API and durable learner state.");

            // Both directories were completely published before cutover. Stop A
            // and start B on the same origin; never overwrite live release files.
            await server.DisposeAsync();
            server = await PublishedServer.Start(b, url, keys, "B");
            foreach (var page in new[] { practice, diagnostic })
            {
                if (await page.EvaluateAsync<string>("window.updateSentinel") != "active-A"
                    || await page.EvaluateAsync<string>("window.deploymentTestRelease") != "A")
                    throw new Exception("An active client was replaced during deployment.");
                await Expect(page.GetByLabel("Theme", new() { Exact = true })).ToHaveValueAsync("warm");
            }
            await Expect(practice.GetByRole(AriaRole.Radio, new() { Name = "Noor owns a helmet." })).ToBeCheckedAsync();
            await Expect(diagnostic.GetByRole(AriaRole.Radio).First).ToBeCheckedAsync();
            await Expect(practice.Locator("#question-title")).ToHaveTextAsync(prompt);
            if (navigations != 0) throw new Exception("The update forced a navigation.");
            var diagnosticAfter = await ApiGet(context, $"{url}/api/sessions/{diagnosticId}");
            if (diagnosticAfter.GetProperty("deadline").GetString() != deadline) throw new Exception("Deployment changed the diagnostic deadline.");
            if ((await ApiGet(context, url + "/api/dashboard")).GetRawText() != dashboardA.GetRawText())
                throw new Exception("Persisted progress changed during deployment.");

            await practice.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false }).ClickAsync();
            await Expect(practice.GetByText("That's sound reasoning.", new() { Exact = true })).ToBeVisibleAsync();
            await diagnostic.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false }).ClickAsync();
            await Expect(diagnostic.GetByText("The report received editorial review.", new() { Exact = true })).ToBeVisibleAsync();
            var resumed = await ApiGet(context, $"{url}/api/sessions/{diagnosticId}");
            if (resumed.GetProperty("answered").GetInt32() != 1 || resumed.GetProperty("deadline").GetString() != deadline)
                throw new Exception("Old client could not continue its original diagnostic on B.");
            Console.WriteLine("PASS open A clients on B: no forced reload, selections retained, normal submission and fixed diagnostic deadline.");

            // A new page is a fresh load with the same warm browser cache and storage.
            var fresh = await context.NewPageAsync();
            fresh.PageError += (_, error) => errors.Add(error);
            await fresh.GotoAsync(url);
            await Expect(fresh.GetByRole(AriaRole.Heading, new() { Name = "A clearer way to think." })).ToBeVisibleAsync();
            await CheckReleaseB(fresh, b);
            await Expect(fresh.GetByLabel("Theme", new() { Exact = true })).ToHaveValueAsync("warm");
            if ((await context.CookiesAsync()).Single(c => c.Name == "reasoning-profile").Value != cookie)
                throw new Exception("Learner identity cookie changed during deployment.");
            if ((await ApiGet(context, url + "/api/dashboard")).GetProperty("skills")[0].GetProperty("attempts").GetInt32() != 2)
                throw new Exception("Persisted progress was not retained on B.");
            await CheckHeaders(context, url);
            foreach (var path in new[] { "/", "/manifest.webmanifest", "/css/app.css", "/js/theme.js", "/_framework/ReasoningLab.Client.wasm" })
            {
                var changed = await context.APIRequest.GetAsync(url + path, new() { Headers = new Dictionary<string, string> { ["If-None-Match"] = validators[path] } });
                if (changed.Status != 200) throw new Exception($"B incorrectly reused A's validator: {path}");
            }
            await CheckCaches(fresh);
            await Expect(fresh.Locator("#startup-error")).ToHaveCountAsync(0);
            if (errors.Count != 0) throw new Exception(string.Join(Environment.NewLine, errors));
            await fresh.ScreenshotAsync(new() { Path = "artifacts/update-release-B.png", FullPage = true });
            Console.WriteLine("PASS fresh warm-cache B load: HTML/CSS/JS/manifest and WASM match B; identity, theme and progress survive.");
            await fresh.EvaluateAsync("document.cookie = 'deployment-persistence-probe=1; Max-Age=86400; SameSite=Strict; Path=/'");
            if ((await context.CookiesAsync()).Single(c => c.Name == "reasoning-profile").Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                throw new Exception("Learner cookie is not persistent before browser shutdown.");
            await context.CloseAsync();
            AssertProcessExited(originalProcessId);
            await using var relaunched = await browser.BrowserType.LaunchPersistentContextAsync(profile, new()
            {
                Headless = true, ExecutablePath = Environment.GetEnvironmentVariable("BROWSER_EXECUTABLE")
            });
            var restartedProcessId = await BrowserProcessId(relaunched);
            Console.WriteLine($"Persistent profile restarted: exited PID {originalProcessId}, new PID {restartedProcessId}.");
            if (originalProcessId == restartedProcessId) throw new Exception("Expected a new browser process.");
            var launched = await relaunched.NewPageAsync();
            var reopenedCookies = await relaunched.CookiesAsync();
            var learnerRetained = reopenedCookies.Any(c => c.Name == "reasoning-profile" && c.Value == cookie);
            var probeRetained = reopenedCookies.Any(c => c.Name == "deployment-persistence-probe" && c.Value == "1");
            Console.WriteLine($"Browser relaunch before navigation: learner cookie retained={learnerRetained}; independent persistent probe retained={probeRetained}.");
            await launched.GotoAsync(url);
            await Expect(launched.GetByRole(AriaRole.Heading, new() { Name = "A clearer way to think." })).ToBeVisibleAsync();
            await CheckReleaseB(launched, b);
            await Expect(launched.GetByLabel("Theme", new() { Exact = true })).ToHaveValueAsync("warm");
            var sameIdentity = (await relaunched.CookiesAsync()).Single(c => c.Name == "reasoning-profile").Value == cookie;
            var retainedAttempts = (await ApiGet(relaunched, url + "/api/dashboard")).GetProperty("skills").EnumerateArray().Sum(s => s.GetProperty("attempts").GetInt32());
            restartRetainedIdentity = learnerRetained && probeRetained && sameIdentity && retainedAttempts == 2;
            Console.WriteLine($"Browser relaunch identity retained: {sameIdentity}; attempts: {retainedAttempts}");
            if (!restartRetainedIdentity)
                Console.WriteLine("LIMITATION: this browser process relaunch did not retain learner state; inspect cookie persistence on the target platform.");
            await CheckCaches(launched);
            Console.WriteLine("PASS browser process relaunch: release B and theme retained; identity result reported separately.");
        }
        finally { if (server is not null) await server.DisposeAsync(); }
        await CheckCutoverRace(browser, a, b, url, keys);
        if (!restartRetainedIdentity) throw new Exception("Browser-process restart lost persistent cookies. Check browser encryption/profile access (Windows runner DPAPI failed during investigation). No cookies were restored by the harness; restart continuity remains unverified.");
    }

    private static async Task<int> BrowserProcessId(IBrowserContext context)
    {
        var session = await context.Browser!.NewBrowserCDPSessionAsync();
        try
        {
            var result = await session.SendAsync("SystemInfo.getProcessInfo");
            return result!.Value.GetProperty("processInfo").EnumerateArray()
                .Single(p => p.GetProperty("type").GetString() == "browser").GetProperty("id").GetInt32();
        }
        finally { await session.DetachAsync(); }
    }

    private static void AssertProcessExited(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            if (!process.HasExited) throw new Exception("Original browser is still running after context closure.");
        }
        catch (ArgumentException) { /* The original PID no longer exists. */ }
    }

    private static async Task CheckCutoverRace(IBrowser browser, string a, string b, string url, string keys)
    {
        // Deliberately pause one boot request to model a load spanning deployment.
        // Routing disables HTTP cache in this separate, disposable context only.
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var integrityError = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && message.Text.Contains("integrity", StringComparison.OrdinalIgnoreCase))
                integrityError.TrySetResult(message.Text);
        };
        await page.RouteAsync("**/_framework/ReasoningLab.Client.wasm", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        PublishedServer? server = null;
        try
        {
            server = await PublishedServer.Start(a, url, keys, "A");
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var navigations = 0;
            page.FrameNavigated += (_, frame) => { if (frame == page.MainFrame) navigations++; };
            await server.DisposeAsync();
            server = await PublishedServer.Start(b, url, keys, "B");
            release.TrySetResult();
            var failure = await integrityError.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await File.WriteAllTextAsync("artifacts/update-cutover-race.txt", failure);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "A clearer way to think." })).ToHaveCountAsync(0);
            await Expect(page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "We couldn’t start ReasoningLab" })).ToBeFocusedAsync();
            var reload = page.GetByRole(AriaRole.Button, new() { Name = "Reload application" });
            await Expect(reload).ToBeVisibleAsync();
            // Mobile viewport and keyboard activation exercise the actual recovery UI.
            await page.SetViewportSizeAsync(390, 844);
            if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"))
                throw new Exception("Startup recovery overflows the mobile viewport.");
            await page.ScreenshotAsync(new() { Path = "artifacts/update-startup-recovery.png", FullPage = true });
            if (navigations != 0) throw new Exception("Startup recovery navigated without user action.");
            await page.UnrouteAsync("**/_framework/ReasoningLab.Client.wasm");
            await page.Keyboard.PressAsync("Tab");
            await Expect(reload).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "A clearer way to think." })).ToBeVisibleAsync();
            await CheckReleaseB(page, b);
            await Expect(page.Locator("#startup-error")).ToHaveCountAsync(0);
            Console.WriteLine("PASS simulated boot/cutover race: integrity rejects mixed releases; accessible startup-only recovery and keyboard reload boot B.");
        }
        finally
        {
            release.TrySetResult();
            if (server is not null) await server.DisposeAsync();
        }
    }

    private static async Task CheckReleaseB(IPage page, string directory)
    {
        await Expect(page.Locator("meta[name=deployment-test-release]")).ToHaveAttributeAsync("content", "B");
        if (await page.EvaluateAsync<string>("window.deploymentTestRelease") != "B"
            || await page.EvaluateAsync<string>("getComputedStyle(document.documentElement).getPropertyValue('--deployment-test-release').trim()") != "'B'")
            throw new Exception("A stale application asset survived the fresh B load.");
        await CheckBrowserAssets(page, directory);
    }

    private static async Task CheckBrowserAssets(IPage page, string directory)
    {
        // No routing, cache clearing or cache-busting query strings: use normal
        // browser HTTP cache semantics and compare decoded bytes to the publication.
        foreach (var path in Assets)
        {
            var actual = await page.EvaluateAsync<string>("""
                async path => {
                    const response = await fetch(path);
                    if (!response.ok) throw new Error(`Asset failed: ${path}`);
                    const hash = await crypto.subtle.digest('SHA-256', await response.arrayBuffer());
                    return [...new Uint8Array(hash)].map(b => b.toString(16).padStart(2,'0')).join('');
                }
                """, path);
            if (actual != HashFile(directory, path)) throw new Exception($"Incompatible cached asset: {path}");
        }
    }

    private static string HashFile(string directory, string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "wwwroot", path == "/" ? "index.html" : path.TrimStart('/'))))).ToLowerInvariant();

    private static async Task<Dictionary<string, string>> CheckHeaders(IBrowserContext context, string url)
    {
        var validators = new Dictionary<string, string>();
        foreach (var path in Assets.Append("/practice/general"))
        {
            var response = await context.APIRequest.GetAsync(url + path);
            if (!response.Ok || response.Headers.GetValueOrDefault("cache-control") != "no-cache")
                throw new Exception($"Missing revalidation policy: {path}");
            validators[path] = response.Headers["etag"];
            var conditional = await context.APIRequest.GetAsync(url + path, new() { Headers = new Dictionary<string, string> { ["If-None-Match"] = validators[path] } });
            if (conditional.Status != 304 || conditional.Headers.GetValueOrDefault("cache-control") != "no-cache")
                throw new Exception($"Conditional revalidation failed: {path}");
        }
        var head = await context.APIRequest.HeadAsync(url + "/manifest.webmanifest");
        if (head.Headers.GetValueOrDefault("cache-control") != "no-cache") throw new Exception("HEAD omitted revalidation policy.");
        return validators;
    }

    private static async Task<JsonElement> ApiGet(IBrowserContext context, string url)
    {
        var response = await context.APIRequest.GetAsync(url);
        if (!response.Ok || response.Headers.GetValueOrDefault("cache-control") != "no-store")
            throw new Exception("Learner API response must be successful and no-store.");
        using var body = JsonDocument.Parse(await response.TextAsync());
        return body.RootElement.Clone();
    }

    private static async Task NoStore(IResponse response)
    {
        if (!response.Ok || await response.HeaderValueAsync("cache-control") != "no-store")
            throw new Exception("Learner POST response must remain no-store.");
    }

    private static async Task CheckCaches(IPage page)
    {
        if (await page.EvaluateAsync<int>("navigator.serviceWorker.getRegistrations().then(r => r.length)") != 0)
            throw new Exception("An update registered a service worker.");
        var cachedApi = await page.EvaluateAsync<string[]>("""
            async () => {
                const bad = [];
                for (const name of await caches.keys()) {
                    const cache = await caches.open(name);
                    for (const request of await cache.keys())
                        if (new URL(request.url).pathname.startsWith('/api')) bad.push(request.url);
                }
                return bad;
            }
            """);
        if (cachedApi.Length != 0) throw new Exception("An API response was placed in Cache Storage.");
    }

    private sealed class PublishedServer(Process process, Task<string> stdout, Task<string> stderr, string label) : IAsyncDisposable
    {
        private bool stopped;
        public static async Task<PublishedServer> Start(string directory, string url, string keys, string label)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__ReasoningLab")))
                throw new Exception("Set ConnectionStrings__ReasoningLab to an existing migrated test database.");
            var info = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(Path.Combine(directory, "ReasoningLab.Server.dll"));
            info.ArgumentList.Add("--urls"); info.ArgumentList.Add(url);
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            info.Environment["DataProtectionPath"] = keys;
            info.Environment["Logging__LogLevel__Default"] = "Warning";
            var process = Process.Start(info) ?? throw new Exception("Could not start published server.");
            var server = new PublishedServer(process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync(), label);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                for (var attempt = 0; attempt < 60; attempt++)
                {
                    if (process.HasExited) throw new Exception($"Published {label} exited; inspect artifacts/update-server-{label}.log.");
                    try
                    {
                        using var response = await http.GetAsync(url + "/deployment-probe.json");
                        if (response.IsSuccessStatusCode)
                        {
                            using var probe = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                            if (probe.RootElement.GetProperty("release").GetString() == label) return server;
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
                    await Task.Delay(250);
                }
                throw new Exception($"Published {label} did not start.");
            }
            catch { await server.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            if (stopped) return;
            stopped = true;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await File.WriteAllTextAsync($"artifacts/update-server-{label}.log", await stdout + await stderr);
            process.Dispose();
        }
    }
}
