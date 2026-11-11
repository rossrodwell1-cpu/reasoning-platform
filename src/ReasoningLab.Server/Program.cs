using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ReasoningLab.Server.Application;
using ReasoningLab.Server.Persistence;
using ReasoningLab.Server.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddDbContext<LabDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("ReasoningLab")
    ?? throw new InvalidOperationException("Set ConnectionStrings__ReasoningLab to your PostgreSQL connection string.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<LearningService>();
var protection = builder.Services.AddDataProtection().SetApplicationName("ReasoningLab");
if (builder.Configuration["DataProtectionPath"] is { } keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.WebHost.UseStaticWebAssets();
var app = builder.Build();
if (app.Environment.IsDevelopment() || args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.Initialize(db);
}
if (args.Contains("--migrate")) return;
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.Headers["X-ReasoningLab"] != "1")
        { context.Response.StatusCode = 403; return; }
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}")
        { context.Response.StatusCode = 403; return; }
    }
    else if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
    {
        // Stable asset URLs are retained by the .NET 10 publish workaround.
        // Revalidate all public responses, including fallback HTML and 304s.
        // OnStarting runs after static-file middleware sets its own headers.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            return Task.CompletedTask;
        });
    }
    try { await next(); }
    catch (RuleException ex)
    { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
});
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapLabEndpoints();
app.MapFallbackToFile("index.html");
app.Run();
public partial class Program;
