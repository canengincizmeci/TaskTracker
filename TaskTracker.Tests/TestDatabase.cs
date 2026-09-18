using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Tests;

internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public TestDatabase()
    {
        connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Users.AddRange(User(1), User(2), User(3));
        context.SaveChanges();
    }

    public TaskTrackerDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<TaskTrackerDbContext>()
        .UseSqlite(connection).AddInterceptors(interceptors).Options);

    public static TaskRequest Task(int id = 1) => new()
    {
        Id = id, OwnerId = 1, Title = "Workflow task", Description = "A task for workflow tests",
        Category = "Tests", Activity = true
    };

    private static User User(int id) => new()
    {
        Id = id, FirstName = "Test", LastName = "User", UserName = $"user{id}",
        Email = $"user{id}@example.test", PasswordHash = [1], PasswordSalt = [1], Status = true
    };

    public void Dispose() => connection.Dispose();
}
