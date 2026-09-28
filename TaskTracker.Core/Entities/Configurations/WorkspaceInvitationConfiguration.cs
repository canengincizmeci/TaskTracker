using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class WorkspaceInvitationConfiguration : IEntityTypeConfiguration<WorkspaceInvitation>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvitation> builder)
    {
        builder.ToTable("WorkspaceInvitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.RespondedAt).IsRequired(false);
        builder.Property(x => x.ExpiresAt).IsRequired(false);
        builder.Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(0L);

        builder.HasOne(x => x.Workspace).WithMany(x => x.Invitations)
            .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InvitedUser).WithMany()
            .HasForeignKey(x => x.InvitedUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InvitedByUser).WithMany()
            .HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.WorkspaceId, x.InvitedUserId }).IsUnique()
            .HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(x => new { x.InvitedUserId, x.Status, x.ExpiresAt });
    }
}
