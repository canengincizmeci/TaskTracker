using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Configurations;

public class SubTaskSubmissionReviewConfiguration : IEntityTypeConfiguration<SubTaskSubmissionReview>
{
    public void Configure(EntityTypeBuilder<SubTaskSubmissionReview> builder)
    {
        builder.ToTable("SubTaskSubmissionReviews", table =>
            table.HasCheckConstraint("CK_SubTaskSubmissionReviews_Decision",
                SubTaskCheckSql.InValues(nameof(SubTaskSubmissionReview.Decision), Enum.GetValues<TaskReviewDecision>())));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.SubTaskSubmissionId });
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.Feedback).HasMaxLength(SubTaskSubmissionReview.MaxFeedbackLength);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.SubTaskSubmission).WithOne()
            .HasForeignKey<SubTaskSubmissionReview>(x => x.SubTaskSubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReviewerUser).WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
