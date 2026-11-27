using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TaskTracker.Core.DataAccess;

namespace TaskTracker.API.Services;

internal static class DeploymentSecrets
{
    public static void AddMountedSecrets(this ConfigurationManager configuration)
    {
        if (configuration["TASKTRACKER_SECRETS_DIRECTORY"] is { Length: > 0 } path)
            configuration.AddKeyPerFile(path, optional: false);
    }
}

// EF tools and the migration bundle only need the migrator's database credential.
public sealed class MigrationDbContextFactory : IDesignTimeDbContextFactory<TaskTrackerDbContext>
{
    public TaskTrackerDbContext CreateDbContext(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MigrationDbContextFactory).Assembly.GetName().Name,
            Args = args
        });
        builder.Configuration.AddMountedSecrets();
        var connection = builder.Configuration.GetConnectionString("PostgreSql");
        var options = new DbContextOptionsBuilder<TaskTrackerDbContext>().UseNpgsql(connection).Options;
        return new TaskTrackerDbContext(options);
    }
}
