using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class TaskSubmissionReviewConfiguration : IEntityTypeConfiguration<TaskSubmissionReview>
{
    public void Configure(EntityTypeBuilder<TaskSubmissionReview> builder)
    {
        builder.ToTable("TaskSubmissionReviews", table =>
        {
            table.HasCheckConstraint("CK_TaskSubmissionReviews_Decision", "\"Decision\" IN ('Approved', 'ChangesRequested')");
            table.HasCheckConstraint("CK_TaskSubmissionReviews_Feedback", "\"Decision\" <> 'ChangesRequested' OR (\"Feedback\" IS NOT NULL AND length(trim(\"Feedback\")) > 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Feedback).HasMaxLength(TaskSubmissionReview.MaxFeedbackLength);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.TaskSubmissionId).IsUnique();
        builder.HasOne(x => x.TaskSubmission).WithOne().HasForeignKey<TaskSubmissionReview>(x => x.TaskSubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReviewerUser).WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
