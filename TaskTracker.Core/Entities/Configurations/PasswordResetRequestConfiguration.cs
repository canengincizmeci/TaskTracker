using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations
{
    public class PasswordResetRequestConfiguration : IEntityTypeConfiguration<PasswordResetRequest>
    {
        public void Configure(EntityTypeBuilder<PasswordResetRequest> builder)
        {
            builder.ToTable("PasswordResetRequests");

            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedOnAdd();

            builder.Property(x => x.UserId).IsRequired();
            builder.Property(x => x.CodeHash).IsRequired().HasMaxLength(128);
            builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
            builder.Property(x => x.ExpiresAt).IsRequired();
            builder.Property(x => x.FailedAttemptCount).IsRequired().HasDefaultValue(0);
            builder.Property(x => x.LockedUntil).IsRequired(false);
            builder.Property(x => x.VerifiedAt).IsRequired(false);
            builder.Property(x => x.ResetTokenHash).IsRequired(false).HasMaxLength(128);
            builder.Property(x => x.ResetTokenExpiresAt).IsRequired(false);
            builder.Property(x => x.UsedAt).IsRequired(false);
            builder.Property(x => x.InvalidatedAt).IsRequired(false);

            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.UserId);
            builder.HasIndex(x => x.ResetTokenHash).IsUnique();
            builder.HasIndex(x => x.ExpiresAt);
        }
    }
}
