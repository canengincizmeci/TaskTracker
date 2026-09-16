using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class TaskActivityConfiguration : IEntityTypeConfiguration<TaskActivity>
{
    public void Configure(EntityTypeBuilder<TaskActivity> builder)
    {
        builder.ToTable("TaskActivities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ActivityType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.TaskRequestId, x.CreatedAt, x.Id });
        builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Invitation).WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Review).WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Restrict);
    }
}
