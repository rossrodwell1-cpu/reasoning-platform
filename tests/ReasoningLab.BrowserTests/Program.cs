using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
SetDefaultExpectTimeout(30000);

var url = Environment.GetEnvironmentVariable("BROWSER_TEST_URL") ?? "http://localhost:5180";
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new()
{
    Headless = true,
    ExecutablePath = Environment.GetEnvironmentVariable("BROWSER_EXECUTABLE")
});
Directory.CreateDirectory("artifacts");
if (args.Contains("--updates-only"))
{
    await DeploymentChecks.Run(browser);
    return;
}
if (args.Contains("--timeout-only"))
{
    await RecoveryChecks.TimeoutRecovery(browser, url);
    return;
}
if (args.Contains("--recovery-only"))
{
    await RecoveryChecks.Run(browser, url);
    return;
}
if (args.Contains("--pwa-only"))
{
    await PwaChecks.Run(browser, url);
    return;
}
if (!args.Contains("--themes-only"))
{
foreach (var (name, width, height) in new[] { ("desktop", 1440, 1100), ("tablet", 820, 1180), ("mobile", 390, 844) })
{
    await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height } });
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => { errors.Add(error); Console.WriteLine(error); };
    page.Console += (_, message) => { if (message.Type == "error") Console.WriteLine(message.Text); };
    await page.GotoAsync(url);
    await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "A clearer way to think." })).ToBeVisibleAsync();
    await page.ScreenshotAsync(new() { Path = $"artifacts/{name}-dashboard.png", FullPage = true });
    await page.GetByRole(AriaRole.Link, new() { Name = "Find your next challenge" }).ClickAsync();
    foreach (var option in new[] { "It has a blue label.", "Noor owns a helmet.", "The atlas may not leave the library." })
    {
        await page.GetByText(option, new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Check answer" }).ClickAsync();
        await Expect(page.GetByText("That's sound reasoning.", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = false }).ClickAsync();
    }
    await Expect(page.GetByText("Every parcel marked urgent", new() { Exact = false })).ToBeVisibleAsync();
    await page.ScreenshotAsync(new() { Path = $"artifacts/{name}-practice.png", FullPage = true });
    await page.ReloadAsync();
    await Expect(page.GetByText("Every parcel marked urgent", new() { Exact = false })).ToBeVisibleAsync();
    if (width <= 760) await page.GetByRole(AriaRole.Button, new() { Name = "Menu", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "LSAT preparation", Exact = false }).First.ClickAsync();
    await Expect(page.GetByText("Every funded project", new() { Exact = false })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Start short diagnostic" }).ClickAsync();
    foreach (var option in new[] { "Mara has completed an inspection.", "The report received editorial review.", "T is not directed solely by its applicant." })
    {
        await page.GetByText(option, new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Check answer" }).ClickAsync();
    }
    await Expect(page.GetByText("You answered 3 of 3 questions correctly.")).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "See your progress" }).ClickAsync();
    await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Your diagnostic results" })).ToBeVisibleAsync();
    await page.ReloadAsync();
    await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Your diagnostic results" })).ToBeVisibleAsync();
    // Visible document geometry: responsive layouts must not require horizontal scrolling.
    if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"))
        throw new Exception($"Horizontal overflow at {name} viewport.");
    if (errors.Count > 0) throw new Exception(string.Join(Environment.NewLine, errors));
    Console.WriteLine($"PASS {name}: practice, promotion, refresh persistence, exam switch, diagnostic and dashboard.");
}
}

if (args.Contains("--workflow-only")) return;
await ThemeChecks.Run(browser, url);
await PwaChecks.Run(browser, url);
await RecoveryChecks.Run(browser, url);
