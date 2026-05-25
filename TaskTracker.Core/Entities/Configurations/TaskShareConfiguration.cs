using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Configurations
{
    public class TaskShareConfiguration : IEntityTypeConfiguration<TaskShare>
    {
        public void Configure(EntityTypeBuilder<TaskShare> builder)
        {
            builder.ToTable("TaskShares");

            builder.HasKey(x => x.Id);

            builder.Property(x=>x.Id).ValueGeneratedOnAdd();

            builder.Property(x => x.Permission)
                .IsRequired()
                .HasConversion<int>();

            builder.HasOne(x => x.TaskRequest)
                .WithMany(x => x.TaskShares)
                .HasForeignKey(x => x.TaskRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.SharedWithUser)
                .WithMany(x => x.SharedTaskRequests)
                .HasForeignKey(x => x.SharedWithUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => new { x.TaskRequestId, x.SharedWithUserId })
                .IsUnique();

            builder.Property(x => x.SharedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        }
    }
}
