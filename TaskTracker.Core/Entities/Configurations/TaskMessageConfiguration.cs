using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class TaskMessageConfiguration : IEntityTypeConfiguration<TaskMessage>
{
    public void Configure(EntityTypeBuilder<TaskMessage> builder)
    {
        builder.ToTable("TaskMessages", table => table.HasCheckConstraint(
            "CK_TaskMessages_Content", "length(trim(\"Content\")) > 0 AND length(\"Content\") <= 4000"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Content).HasMaxLength(TaskMessage.MaxContentLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.TaskRequestId, x.CreatedAt, x.Id });
        builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SenderUser).WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
