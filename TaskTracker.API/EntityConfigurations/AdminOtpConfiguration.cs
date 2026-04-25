using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.API.Entitites;

namespace TaskTracker.API.EntityConfigurations
{
    public class AdminOtpConfiguration : IEntityTypeConfiguration<AdminOtp>
    {
        public void Configure(EntityTypeBuilder<AdminOtp> builder)
        {
            builder.ToTable("AdminOtps");

            builder.HasKey(ao => ao.Id);
            builder.Property(ao => ao.Id).ValueGeneratedOnAdd();

            builder.Property(ao => ao.AdminId)
                   .IsRequired();

            builder.Property(ao => ao.Code)
                   .IsRequired()
                   .HasMaxLength(500);

            builder.Property(ao => ao.CreatedTime)
                   .IsRequired();

            builder.Property(ao => ao.ExpireTime)
                   .IsRequired();

            builder.Property(ao => ao.IsUsed)
                   .IsRequired()
                   .HasDefaultValue(false);

            builder.Property(ao => ao.FailedAttemptCount)
                   .IsRequired()
                   .HasDefaultValue(0);

            builder.HasOne(ao => ao.Admin)
                   .WithMany(a => a.AdminOtps)
                   .HasForeignKey(ao => ao.AdminId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(ao => ao.AdminId);

            builder.HasIndex(ao => ao.Code);




        }
    }
}
