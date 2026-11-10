using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

internal static class RecoveryChecks
{
    private const string Answers = "**/api/sessions/*/answers";

    public static async Task Run(IBrowser browser, string url)
    {
        foreach (var failure in new[] { "before-send", "lost-response", "unavailable", "invalid-response", "rejected" })
        {
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
            var page = await context.NewPageAsync();
            await OpenQuestion(page, url);
            var payloads = new List<string>();
            var targets = new List<string>();
            var calls = 0;
            await page.RouteAsync(Answers, async route =>
            {
                payloads.Add(route.Request.PostData!);
                targets.Add(route.Request.Url);
                calls++;
                if (calls == 1)
                {
                    switch (failure)
                    {
                        case "before-send": await route.AbortAsync("internetdisconnected"); return;
                        case "lost-response":
                            var committed = await route.FetchAsync();
                            if (!committed.Ok) throw new Exception("Expected a committed answer before dropping its response.");
                            await route.AbortAsync("connectionreset"); return;
                        case "unavailable": await route.FulfillAsync(new() { Status = 503, Body = "Service unavailable" }); return;
                        case "invalid-response": await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "not-json" }); return;
                        case "rejected": await route.FulfillAsync(new() { Status = 409, ContentType = "application/json", Body = "{\"error\":\"This answer is already recorded.\"}" }); return;
                    }
                }
                // An additional failed retry proves that pending identifiers survive more than one failure.
                if (calls == 2 && failure != "rejected") { await route.AbortAsync(); return; }
                await route.ContinueAsync();
            });

            await page.GetByRole(AriaRole.Radio).First.CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false }).ClickAsync();
            var error = page.GetByTestId("session-error");
            await Expect(error).ToContainTextAsync(failure == "rejected" ? "The server rejected this submission" : "We couldn't confirm whether your answer was saved");
            await Expect(page.GetByRole(AriaRole.Radio).First).ToBeCheckedAsync();
            await Expect(page.GetByRole(AriaRole.Radio).First).ToBeDisabledAsync();
            await Expect(page.Locator(".feedback")).ToHaveCountAsync(0);
            if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"))
                throw new Exception("Recovery message overflows the mobile viewport.");
            if (failure == "lost-response")
                await page.ScreenshotAsync(new() { Path = "artifacts/recovery-uncertain-mobile.png", FullPage = true });
            if (failure == "before-send" && !await page.EvaluateAsync<bool>("navigator.onLine"))
                throw new Exception("This case must simulate an unreachable API while the browser reports online.");
            if (await Attempts(context, url) != (failure == "lost-response" ? 1 : 0)) throw new Exception("Unexpected attempts before recovery.");

            if (failure == "rejected")
            {
                await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry same answer" })).ToHaveCountAsync(0);
                await page.GetByRole(AriaRole.Button, new() { Name = "Refresh session", Exact = true }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Radio).First).ToBeEnabledAsync();
                await Expect(page.GetByRole(AriaRole.Radio).First).ToBeCheckedAsync();
                if (calls != 1) throw new Exception("Session refresh unexpectedly submitted an answer.");
            }
            else
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Retry same answer" }).ClickAsync();
                await Expect(error).ToContainTextAsync("We couldn't confirm");
                await page.GetByRole(AriaRole.Button, new() { Name = "Retry same answer" }).ClickAsync();
                await Expect(page.GetByText("That's sound reasoning.", new() { Exact = true })).ToBeVisibleAsync();
                await Expect(error).ToHaveCountAsync(0);
                if (payloads.Distinct().Count() != 1 || targets.Distinct().Count() != 1)
                    throw new Exception("A retry changed the answer, assignment or session.");
                if (calls != 3 || await Attempts(context, url) != 1) throw new Exception("Retry duplicated an attempt.");
            }
            Console.WriteLine($"PASS recovery {failure}: selection preserved, explicit recovery and correct attempt count.");
        }

        await DashboardFailure(browser, url);
        await DiagnosticRecovery(browser, url);
        await DiagnosticClockRecovery(browser, url);
        await TimeoutRecovery(browser, url);
    }

    public static async Task TimeoutRecovery(IBrowser browser, string url)
    {
        foreach (var diagnostic in new[] { false, true })
        {
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await OpenQuestion(page, url, diagnostic ? "lsat" : "general");
            if (diagnostic)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Start short diagnostic", Exact = false }).ClickAsync();
                await Expect(page.GetByText("Mara has completed an inspection.", new() { Exact = true })).ToBeVisibleAsync();
            }

            var committed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var payloads = new List<string>();
            var targets = new List<string>();
            await page.RouteAsync(Answers, async route =>
            {
                targets.Add(route.Request.Url);
                payloads.Add(route.Request.PostData!);
                if (targets.Count > 1) { await route.ContinueAsync(); return; }
                try
                {
                    var response = await route.FetchAsync();
                    if (!response.Ok) throw new Exception("Expected successful server save before the timeout.");
                    committed.SetResult(await response.TextAsync());
                    // Do not abort or fabricate HTTP 408. Withhold the real response
                    // until the unchanged client's 20-second cancellation fires.
                    await release.Task;
                    await route.AbortAsync();
                }
                catch (Exception ex) { committed.TrySetException(ex); throw; }
                finally { released.TrySetResult(); }
            });

            try
            {
                await page.GetByRole(AriaRole.Radio).First.CheckAsync();
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                await page.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false }).ClickAsync();
                using var saved = JsonDocument.Parse(await committed.Task.WaitAsync(TimeSpan.FromSeconds(15)));
                if (saved.RootElement.GetProperty("answered").GetInt32() != 1)
                    throw new Exception("The server did not confirm one saved answer before the timeout.");
                var error = page.GetByTestId("session-error");
                await Expect(error).ToContainTextAsync("The request timed out.", new() { Timeout = 35000 });
                await Expect(error).ToContainTextAsync("We couldn't confirm whether your answer was saved");
                if (elapsed.Elapsed < TimeSpan.FromSeconds(18)) throw new Exception("The real timeout was not exercised.");
                await Expect(error).Not.ToContainTextAsync("rejected");
                await Expect(error).Not.ToContainTextAsync("was not saved");
                await Expect(page.Locator(".feedback")).ToHaveCountAsync(0);
                await Expect(page.GetByRole(AriaRole.Radio).First).ToBeCheckedAsync();
                await Expect(page.GetByRole(AriaRole.Radio).First).ToBeDisabledAsync();
                if (targets.Count != 1) throw new Exception("A timeout automatically resubmitted the answer.");
                var sessionUrl = targets[0][..^"/answers".Length];
                var before = await context.APIRequest.GetAsync(sessionUrl);
                using var beforeRetry = JsonDocument.Parse(await before.TextAsync());
                if (beforeRetry.RootElement.GetProperty("answered").GetInt32() != 1)
                    throw new Exception("The committed answer was lost before retry.");

                release.SetResult();
                await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await page.GetByRole(AriaRole.Button, new() { Name = "Retry same answer" }).ClickAsync();
                if (diagnostic)
                    await Expect(page.GetByText("The report received editorial review.", new() { Exact = true })).ToBeVisibleAsync();
                else
                    await Expect(page.GetByText("That's sound reasoning.", new() { Exact = true })).ToBeVisibleAsync();
                await Expect(error).ToHaveCountAsync(0);
                if (targets.Count != 2 || targets.Distinct().Count() != 1 || payloads.Distinct().Count() != 1)
                    throw new Exception("Timeout retry changed the session, assignment or answer.");
                var after = await context.APIRequest.GetAsync(sessionUrl);
                using var recovered = JsonDocument.Parse(await after.TextAsync());
                if (recovered.RootElement.GetProperty("answered").GetInt32() != 1)
                    throw new Exception("Timeout recovery duplicated an attempt.");
                if (diagnostic)
                {
                    var deadline = saved.RootElement.GetProperty("deadline").GetDateTimeOffset();
                    if (recovered.RootElement.GetProperty("deadline").GetDateTimeOffset() != deadline
                        || recovered.RootElement.GetProperty("serverNow").GetDateTimeOffset() - saved.RootElement.GetProperty("serverNow").GetDateTimeOffset() < TimeSpan.FromSeconds(18))
                        throw new Exception("Diagnostic time was paused or its deadline changed during timeout recovery.");
                    await Expect(page.Locator(".feedback")).ToHaveCountAsync(0);
                }
                else if (await Attempts(context, url) != 1) throw new Exception("Timeout retry duplicated mastery evidence.");
                Console.WriteLine($"PASS real 20-second timeout after committed {(diagnostic ? "diagnostic" : "practice")} answer: uncertain message, identical retry, one attempt, {(diagnostic ? "unchanged server deadline" : "confirmed feedback restored")}.");
            }
            finally
            {
                release.TrySetResult();
                await page.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.IgnoreErrors });
            }
        }
    }

    private static async Task DashboardFailure(IBrowser browser, string url)
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenQuestion(page, url);
        var writes = 0;
        page.Request += (_, request) => { if (request.Url.EndsWith("/answers")) writes++; };
        await page.RouteAsync("**/api/dashboard", route => route.AbortAsync());
        await page.GetByRole(AriaRole.Radio).First.CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false }).ClickAsync();
        await Expect(page.GetByText("That's sound reasoning.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByTestId("progress-error")).ToContainTextAsync("Your answer was saved");
        await Expect(page.GetByTestId("session-error")).ToHaveCountAsync(0);
        await page.ScreenshotAsync(new() { Path = "artifacts/recovery-confirmed-progress-error.png", FullPage = true });
        await page.UnrouteAsync("**/api/dashboard");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh progress", Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("progress-error")).ToHaveCountAsync(0);
        await Expect(page.GetByText("That's sound reasoning.", new() { Exact = true })).ToBeVisibleAsync();
        if (writes != 1 || await Attempts(context, url) != 1) throw new Exception("Dashboard recovery resubmitted an answer.");
        Console.WriteLine("PASS confirmed answer remains confirmed after dashboard failure; progress retry performs no writes.");
    }

    private static async Task DiagnosticRecovery(IBrowser browser, string url)
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await OpenQuestion(page, url, "lsat");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start short diagnostic", Exact = false }).ClickAsync();
        await Expect(page.GetByText("Mara has completed an inspection.", new() { Exact = true })).ToBeVisibleAsync();
        string? target = null, payload = null, deadline = null;
        var count = 0;
        await page.RouteAsync(Answers, async route =>
        {
            count++;
            if (count == 1)
            {
                target = route.Request.Url; payload = route.Request.PostData;
                var response = await route.FetchAsync();
                using var body = JsonDocument.Parse(await response.TextAsync());
                deadline = body.RootElement.GetProperty("deadline").GetString();
                await route.AbortAsync();
            }
            else
            {
                if (route.Request.Url != target || route.Request.PostData != payload) throw new Exception("Diagnostic retry changed payload.");
                await route.ContinueAsync();
            }
        });
        await page.GetByText("Mara has completed an inspection.", new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false }).ClickAsync();
        await Expect(page.GetByTestId("session-error")).ToContainTextAsync("We couldn't confirm");
        // A device clock jump must not replay a pending answer or manufacture a
        // result. The real server clock is unchanged; server expiry is tested in xUnit.
        await page.Clock.FastForwardAsync(240_000);
        if (count != 1) throw new Exception("The timer automatically resubmitted an uncertain answer.");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Start short diagnostic", Exact = false })).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry same answer" }).ClickAsync();
        await Expect(page.GetByText("The report received editorial review.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator(".feedback")).ToHaveCountAsync(0);
        var sessionResponse = await context.APIRequest.GetAsync(target![..^"/answers".Length]);
        using var session = JsonDocument.Parse(await sessionResponse.TextAsync());
        if (session.RootElement.GetProperty("answered").GetInt32() != 1 || session.RootElement.GetProperty("deadline").GetString() != deadline)
            throw new Exception("Diagnostic retry duplicated an answer or extended its deadline.");
        Console.WriteLine("PASS lost diagnostic response: same identifiers, one recorded answer, fixed deadline and no early feedback.");
    }

    private static async Task DiagnosticClockRecovery(IBrowser browser, string url)
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await OpenQuestion(page, url, "lsat");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start short diagnostic", Exact = false }).ClickAsync();
        await Expect(page.GetByText("Mara has completed an inspection.", new() { Exact = true })).ToBeVisibleAsync();
        var reads = 0;
        var writes = 0;
        page.Request += (_, request) =>
        {
            if (request.Method == "GET" && request.Url.Contains("/api/sessions/")) reads++;
            if (request.Url.EndsWith("/answers")) writes++;
        };
        // The server still has time remaining. Each client clock jump should
        // reconcile once and re-arm its timer, rather than leaving a locked page.
        for (var jump = 0; jump < 2; jump++)
        {
            var response = page.WaitForResponseAsync(r => r.Request.Method == "GET" && r.Url.Contains("/api/sessions/"));
            await page.Clock.FastForwardAsync(240_000);
            await response;
            await Expect(page.GetByText("Session status refreshed. Only answers recorded by the server count.")).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Radio).First).ToBeEnabledAsync();
        }
        if (reads != 2 || writes != 0) throw new Exception("Expiry reconciliation must only read server state.");
        Console.WriteLine("PASS diagnostic clock correction: server authority restored and expiry checks re-armed without writes.");
    }

    private static async Task OpenQuestion(IPage page, string url, string pathway = "general")
    {
        await page.GotoAsync(url + "/practice/" + pathway);
        await Expect(page.GetByRole(AriaRole.Radio).First).ToBeEnabledAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Check answer", Exact = false })).ToBeVisibleAsync();
    }

    private static async Task<int> Attempts(IBrowserContext context, string url)
    {
        var response = await context.APIRequest.GetAsync(url + "/api/dashboard");
        if (!response.Ok) throw new Exception("Could not verify persisted attempts.");
        using var body = JsonDocument.Parse(await response.TextAsync());
        return body.RootElement.GetProperty("skills").EnumerateArray().Sum(skill => skill.GetProperty("attempts").GetInt32());
    }
}
