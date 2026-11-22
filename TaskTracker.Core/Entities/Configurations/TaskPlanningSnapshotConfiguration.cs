using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class TaskPlanningSnapshotConfiguration : IEntityTypeConfiguration<TaskPlanningSnapshot>
{
    public void Configure(EntityTypeBuilder<TaskPlanningSnapshot> builder)
    {
        builder.ToTable("TaskPlanningSnapshots", table =>
        {
            table.HasCheckConstraint(
                "CK_TaskPlanningSnapshots_UtcOffsetMinutes",
                "\"InitialUtcOffsetMinutes\" BETWEEN -720 AND 840");
            table.HasCheckConstraint(
                "CK_TaskPlanningSnapshots_TextLengths",
                "\"InitialTitleLength\" > 0 AND \"InitialTitleLength\" <= 150 AND " +
                "\"InitialDescriptionLength\" > 0 AND \"InitialDescriptionLength\" <= 10000");
            table.HasCheckConstraint(
                "CK_TaskPlanningSnapshots_DeadlineConsistency",
                "(\"InitialDueDate\" IS NULL AND \"OriginalDueAtUtc\" IS NULL AND " +
                "\"InitialPlannedCalendarDays\" IS NULL AND \"InitialPlannedDurationSeconds\" IS NULL) OR " +
                "(\"InitialDueDate\" IS NOT NULL AND \"OriginalDueAtUtc\" IS NOT NULL AND " +
                "\"InitialPlannedCalendarDays\" IS NOT NULL AND \"InitialPlannedDurationSeconds\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.TaskRequestId).IsRequired();
        builder.Property(x => x.SchemaVersion)
            .IsRequired()
            .HasMaxLength(TaskPlanningSnapshot.MaxSchemaVersionLength);
        builder.Property(x => x.CreatedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.InitialUtcOffsetMinutes).IsRequired();
        builder.Property(x => x.InitialPriority)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(x => x.InitialDueDate).HasColumnType("date");
        builder.Property(x => x.OriginalDueAtUtc);
        builder.Property(x => x.InitialPlannedCalendarDays);
        builder.Property(x => x.InitialPlannedDurationSeconds);
        builder.Property(x => x.InitialAssignmentState)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(x => x.IsWorkspaceTask).IsRequired();
        builder.Property(x => x.InitialVisibility)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(x => x.InitialTitleLength).IsRequired();
        builder.Property(x => x.InitialDescriptionLength).IsRequired();

        builder.HasIndex(x => x.TaskRequestId).IsUnique();
        builder.HasIndex(x => new { x.CreatedAtUtc, x.Id });
        builder.HasOne(x => x.TaskRequest)
            .WithMany()
            .HasForeignKey(x => x.TaskRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
