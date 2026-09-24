using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TASKTRACKER_TEST_DATABASE")))
            Skip = "Set TASKTRACKER_TEST_DATABASE to a disposable PostgreSQL server connection.";
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class PostgreSqlTheoryAttribute : TheoryAttribute
{
    public PostgreSqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TASKTRACKER_TEST_DATABASE")))
            Skip = "Set TASKTRACKER_TEST_DATABASE to a disposable PostgreSQL server connection.";
    }
}

internal sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    private readonly string adminConnection;
    private readonly string databaseName;
    private readonly string databaseConnection;

    private PostgreSqlTestDatabase(string adminConnection, string databaseName, string databaseConnection)
    {
        this.adminConnection = adminConnection;
        this.databaseName = databaseName;
        this.databaseConnection = databaseConnection;
    }

    public static async Task<PostgreSqlTestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable("TASKTRACKER_TEST_DATABASE")
            ?? throw new InvalidOperationException("TASKTRACKER_TEST_DATABASE is required.");
        var name = $"tasktracker_submission_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Pooling = false }.ConnectionString;
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }
        var database = new NpgsqlConnectionStringBuilder(configured) { Database = name, Pooling = false }.ConnectionString;
        var instance = new PostgreSqlTestDatabase(admin, name, database);
        await using var context = instance.CreateContext();
        await context.Database.MigrateAsync();
        context.Users.AddRange(User(1), User(2), User(3));
        await context.SaveChangesAsync();
        return instance;
    }

    public TaskTrackerDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<TaskTrackerDbContext>().UseNpgsql(databaseConnection)
            .AddInterceptors(interceptors).Options);

    private static User User(int id) => new()
    {
        Id = id, FirstName = "Postgres", LastName = "User", UserName = $"pg-user{id}",
        Email = $"pg-user{id}@example.test", PasswordHash = [1], PasswordSalt = [1], Status = true
    };

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class SaveBarrierInterceptor(string marker, int participants = 2) : DbCommandInterceptor
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;

    private async ValueTask WaitAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (!command.CommandText.Contains(marker, StringComparison.OrdinalIgnoreCase)) return;
        if (Interlocked.Increment(ref arrivals) == participants) ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await WaitAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await WaitAsync(command, cancellationToken);
        return result;
    }
}
