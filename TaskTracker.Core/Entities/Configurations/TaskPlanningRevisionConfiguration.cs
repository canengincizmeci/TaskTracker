using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class TaskPlanningRevisionConfiguration : IEntityTypeConfiguration<TaskPlanningRevision>
{
    public void Configure(EntityTypeBuilder<TaskPlanningRevision> builder)
    {
        builder.ToTable("TaskPlanningRevisions", table => table.HasCheckConstraint(
            "CK_TaskPlanningRevisions_RevisionNumber", "\"RevisionNumber\" > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.TaskRequestId).IsRequired();
        builder.Property(x => x.RevisionNumber).IsRequired();
        builder.Property(x => x.ChangedByUserId).IsRequired();
        builder.Property(x => x.ChangedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.Title).IsRequired().HasMaxLength(TaskRequest.MaxTitleLength);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(TaskRequest.MaxDescriptionLength);
        builder.Property(x => x.Category).IsRequired().HasMaxLength(TaskRequest.MaxCategoryLength);
        builder.Property(x => x.Priority).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.DueDate).HasColumnType("date");

        builder.HasIndex(x => new { x.TaskRequestId, x.RevisionNumber }).IsUnique();
        builder.HasIndex(x => new { x.TaskRequestId, x.ChangedAt, x.Id });
        builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ChangedByUser).WithMany().HasForeignKey(x => x.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
