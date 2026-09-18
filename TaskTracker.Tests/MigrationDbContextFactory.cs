using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TaskTracker.Core.DataAccess;

namespace TaskTracker.Tests;

// Allows migration generation without starting the API or loading its secrets.
// Port 1 is deliberately unusable: database operations require an explicit test connection.
public sealed class MigrationDbContextFactory : IDesignTimeDbContextFactory<TaskTrackerDbContext>
{
    public TaskTrackerDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TASKTRACKER_TEST_DATABASE")
            ?? "Host=127.0.0.1;Port=1;Database=tasktracker_schema_test;Username=tasktracker_test";
        return new TaskTrackerDbContext(new DbContextOptionsBuilder<TaskTrackerDbContext>()
            .UseNpgsql(connection).Options);
    }
}
