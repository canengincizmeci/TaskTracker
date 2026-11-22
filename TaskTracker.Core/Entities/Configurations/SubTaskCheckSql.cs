using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

internal static class SubTaskCheckSql
{
    // The Unicode whitespace recognized by string.IsNullOrWhiteSpace.
    private const string Whitespace = " \t\n\v\f\r\u0085\u00a0\u1680\u2000\u2001\u2002\u2003" +
        "\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";

    internal static void ConfigureNonBlankChecks(ModelBuilder builder, bool isPostgreSql)
    {
        // Encode the trim set in SQL so migration scaffolding never embeds control
        // characters or Unicode line separators in the generated C# string literals.
        var trimCharacters = isPostgreSql
            ? "U&'" + string.Concat(Whitespace.Select(c => $"\\{(int)c:X4}")) + "'"
            : "char(" + string.Join(", ", Whitespace.Select(c => (int)c)) + ")";
        string NonBlank(string column) => $"length(trim(\"{column}\", {trimCharacters})) > 0";

        builder.Entity<SubTask>().ToTable(table =>
        {
            table.HasCheckConstraint("CK_SubTasks_Title", NonBlank(nameof(SubTask.Title)));
            table.HasCheckConstraint("CK_SubTasks_Description", NonBlank(nameof(SubTask.Description)));
        });
        builder.Entity<SubTaskSubmission>().ToTable(table =>
            table.HasCheckConstraint("CK_SubTaskSubmissions_Content", NonBlank(nameof(SubTaskSubmission.Content))));
        builder.Entity<SubTaskSubmissionReview>().ToTable(table =>
            table.HasCheckConstraint("CK_SubTaskSubmissionReviews_Feedback",
                "\"Decision\" <> 'ChangesRequested' OR (\"Feedback\" IS NOT NULL AND " +
                NonBlank(nameof(SubTaskSubmissionReview.Feedback)) + ")"));
    }

    internal static string InValues<T>(string column, IEnumerable<T> values) where T : struct, Enum =>
        $"\"{column}\" IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";
}
