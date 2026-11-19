using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public sealed class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => new { x.TaskRequestId, x.CreatedAt, x.Id });
        builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UploaderUser).WithMany().HasForeignKey(x => x.UploaderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TaskMessage).WithMany().HasForeignKey(x => x.TaskMessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TaskSubmission).WithMany().HasForeignKey(x => x.TaskSubmissionId).OnDelete(DeleteBehavior.Restrict);
    }
}
