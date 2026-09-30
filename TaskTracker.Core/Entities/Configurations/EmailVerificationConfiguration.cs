using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public sealed class EmailVerificationConfiguration : IEntityTypeConfiguration<EmailVerification>
{
    public void Configure(EntityTypeBuilder<EmailVerification> builder)
    {
        builder.Property(x => x.Version)
            .IsConcurrencyToken()
            .HasDefaultValue(0L);

        builder.Property(x => x.DeliveryClaimed)
            .HasDefaultValue(false);

        builder.Property(x => x.DeliveryToken)
            .IsConcurrencyToken();

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("UX_EmailVerifications_UserId_Active")
            .IsUnique()
            .HasFilter("\"IsVerified\" = FALSE");
    }
}
