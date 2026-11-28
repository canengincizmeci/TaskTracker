using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class TaskMlConsentEventConfiguration : IEntityTypeConfiguration<TaskMlConsentEvent>
{
    public void Configure(EntityTypeBuilder<TaskMlConsentEvent> builder)
    {
        builder.Property(x => x.PolicyVersion).HasMaxLength(TaskMlConsentEvent.MaxPolicyVersionLength);
        builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TaskRequestId, x.OccurredAtUtc, x.Id });
    }
}
