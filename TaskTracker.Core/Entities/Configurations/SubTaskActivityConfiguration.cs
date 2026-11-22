using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Core.Entities.Configurations;

public class SubTaskActivityConfiguration : IEntityTypeConfiguration<SubTaskActivity>
{
    public void Configure(EntityTypeBuilder<SubTaskActivity> builder)
    {
        builder.ToTable("SubTaskActivities", table =>
        {
            table.HasCheckConstraint("CK_SubTaskActivities_Type",
                SubTaskCheckSql.InValues(nameof(SubTaskActivity.ActivityType), SubTaskActivity.SupportedTypes));
            table.HasCheckConstraint("CK_SubTaskActivities_FromStatus",
                SubTaskCheckSql.InValues(nameof(SubTaskActivity.FromStatus), Enum.GetValues<TaskStatus>()));
            table.HasCheckConstraint("CK_SubTaskActivities_ToStatus",
                SubTaskCheckSql.InValues(nameof(SubTaskActivity.ToStatus), Enum.GetValues<TaskStatus>()));
            table.HasCheckConstraint("CK_SubTaskActivities_DeletionStatus",
                "\"ActivityType\" <> 'TaskDeleted' OR (\"FromStatus\" IS NULL AND \"ToStatus\" IS NULL)");
            table.HasCheckConstraint("CK_SubTaskActivities_Target",
                "(\"ActivityType\" IN ('UserAssigned', 'UserUnassigned')) = (\"TargetUserId\" IS NOT NULL)");
            table.HasCheckConstraint("CK_SubTaskActivities_Submission",
                "(\"ActivityType\" IN ('SubmissionCreated', 'ChangesRequested', 'SubmissionApproved')) = (\"SubmissionId\" IS NOT NULL)");
            table.HasCheckConstraint("CK_SubTaskActivities_Review",
                "(\"ActivityType\" IN ('ChangesRequested', 'SubmissionApproved')) = (\"ReviewId\" IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ActivityType).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.TaskRequestId, x.CreatedAt, x.Id });
        builder.HasIndex(x => new { x.SubTaskId, x.CreatedAt, x.Id });
        builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SubTask).WithMany()
            .HasForeignKey(x => new { x.SubTaskId, x.TaskRequestId })
            .HasPrincipalKey(x => new { x.Id, x.TaskRequestId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Submission).WithMany()
            .HasForeignKey(x => new { x.SubmissionId, x.SubTaskId })
            .HasPrincipalKey(x => new { x.Id, x.SubTaskId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Review).WithMany()
            .HasForeignKey(x => new { x.ReviewId, x.SubmissionId })
            .HasPrincipalKey(x => new { x.Id, x.SubTaskSubmissionId }).OnDelete(DeleteBehavior.Restrict);
    }
}
