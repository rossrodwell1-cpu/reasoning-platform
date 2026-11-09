using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ReasoningLab.Server.Persistence;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ReasoningLab.Tests;

public class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public class PostgresFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string database = "reasoning_tests_" + Guid.NewGuid().ToString("N");
    private readonly string admin = Environment.GetEnvironmentVariable("TEST_POSTGRES")
        ?? "Host=127.0.0.1;Port=55432;Database=postgres;Username=reasoning";
    public TestClock Clock { get; } = new();
    public string Connection => new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ReasoningLab"] = Connection,
            ["AdminToken"] = "integration-test-token",
            ["DataProtectionPath"] = Path.Combine(Path.GetTempPath(), database + "_keys")
        }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", conn);
        await create.ExecuteNonQueryAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        await db.Database.MigrateAsync();
        await SeedData.Initialize(db);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", conn);
        await drop.ExecuteNonQueryAsync();
    }

    public async Task<int> CorrectAnswer(Guid id)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LabDbContext>().Items.Where(i => i.Id == id).Select(i => i.Question.Answer).SingleAsync();
    }
}
