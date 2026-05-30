using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations
{
    public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
    {
        public void Configure(EntityTypeBuilder<Notification> builder)
        {
            builder.ToTable("Notifications");

            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedOnAdd();

            builder.Property(x => x.UserId).IsRequired();

            builder.Property(x => x.Type).IsRequired().HasConversion<int>();

            builder.Property(x => x.Title).IsRequired().HasMaxLength(150);

            builder.Property(x => x.Message).IsRequired().HasMaxLength(1000);

            builder.Property(x => x.IsRead).IsRequired().HasDefaultValue(false);

            builder.Property(x => x.RelatedEntityId).IsRequired(false);

            builder.Property(x => x.RedirectUrl).IsRequired(false).HasMaxLength(500);

            builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(x => x.ReadAt).IsRequired(false);

            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.UserId);

            builder.HasIndex(x => new
            {
                x.UserId,
                x.IsRead,
                x.CreatedAt
            });
        }
    }
}
