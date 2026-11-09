using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ReasoningLab.Server.Persistence;

public class DesignTimeFactory : IDesignTimeDbContextFactory<LabDbContext>
{
    public LabDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<LabDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__ReasoningLab")
            ?? "Host=localhost;Database=reasoninglab;Username=reasoning").Options);
}
