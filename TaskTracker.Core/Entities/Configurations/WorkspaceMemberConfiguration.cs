using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("WorkspaceMembers", table => table.HasCheckConstraint(
            "CK_WorkspaceMembers_ActiveRemoval",
            "(\"IsActive\" = TRUE AND \"RemovedAt\" IS NULL) OR (\"IsActive\" = FALSE AND \"RemovedAt\" IS NOT NULL)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Role).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.JoinedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.RemovedAt).IsRequired(false);
        builder.Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(0L);

        builder.HasOne(x => x.Workspace).WithMany(x => x.Members)
            .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.IsActive, x.WorkspaceId });
        builder.HasIndex(x => x.WorkspaceId).IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"Role\" = 'Owner'");
    }
}
