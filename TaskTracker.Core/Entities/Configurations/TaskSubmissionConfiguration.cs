using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class TaskSubmissionConfiguration : IEntityTypeConfiguration<TaskSubmission>
{
    public void Configure(EntityTypeBuilder<TaskSubmission> builder)
    {
        builder.ToTable("TaskSubmissions", table =>
        {
            table.HasCheckConstraint("CK_TaskSubmissions_RevisionNumber", "\"RevisionNumber\" > 0");
            table.HasCheckConstraint("CK_TaskSubmissions_Content", "length(trim(\"Content\")) > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Content).IsRequired().HasMaxLength(TaskSubmission.MaxContentLength);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.TaskRequestId, x.RevisionNumber }).IsUnique();
        builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SubmittedByUser).WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
