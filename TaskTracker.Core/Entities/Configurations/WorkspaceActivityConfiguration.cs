using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class WorkspaceActivityConfiguration : IEntityTypeConfiguration<WorkspaceActivity>
{
    public void Configure(EntityTypeBuilder<WorkspaceActivity> builder)
    {
        builder.ToTable("WorkspaceActivities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ActivityType).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.FromRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ToRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedAt, x.Id });
        builder.HasOne(x => x.Workspace).WithMany(x => x.Activities)
            .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetUser).WithMany()
            .HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
