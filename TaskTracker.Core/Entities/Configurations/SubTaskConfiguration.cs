using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Core.Entities.Configurations;

public class SubTaskConfiguration : IEntityTypeConfiguration<SubTask>
{
    public void Configure(EntityTypeBuilder<SubTask> builder)
    {
        builder.ToTable("SubTasks", table =>
        {
            table.HasCheckConstraint("CK_SubTasks_Priority", SubTaskCheckSql.InValues(nameof(SubTask.Priority), Enum.GetValues<TaskPriority>()));
            table.HasCheckConstraint("CK_SubTasks_Status", SubTaskCheckSql.InValues(nameof(SubTask.Status), Enum.GetValues<TaskStatus>()));
            table.HasCheckConstraint("CK_SubTasks_Version", "\"Version\" >= 0");
            table.HasCheckConstraint("CK_SubTasks_DeletionAudit",
                "(\"Activity\" = TRUE AND \"DeletedAt\" IS NULL AND \"DeletedByUserId\" IS NULL) OR " +
                "(\"Activity\" = FALSE AND \"DeletedAt\" IS NOT NULL AND \"DeletedByUserId\" IS NOT NULL AND \"DeletedAt\" >= \"CreatedAt\")");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TaskRequestId });
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(SubTask.MaxTitleLength);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(SubTask.MaxDescriptionLength);
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.DueDate).HasColumnType("date");
        builder.Property(x => x.Activity).HasDefaultValue(true);
        builder.Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(0L);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP")
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.CreatedByUserId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasIndex(x => new { x.TaskRequestId, x.CreatedAt, x.Id });
        builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
