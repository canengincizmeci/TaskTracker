using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Configurations
{
    public class TaskRequestConfiguration : IEntityTypeConfiguration<TaskRequest>
    {
        public void Configure(EntityTypeBuilder<TaskRequest> builder)
        {
            builder.ToTable("TaskRequests");

            builder.HasKey(tr => tr.Id);
            builder.Property(tr => tr.Id).ValueGeneratedOnAdd();

            builder.Property(tr => tr.Title).IsRequired().HasMaxLength(150);

            builder.Property(tr => tr.Description).IsRequired().HasMaxLength(10000);

            builder.Property(tr => tr.Category).IsRequired().HasMaxLength(150);

            builder.Property(tr => tr.Priority).IsRequired().HasConversion<string>().HasMaxLength(50);

            builder.Property(tr => tr.Status).IsRequired().HasMaxLength(50).HasConversion<string>().HasMaxLength(50);

            builder.Property(tr => tr.Activity).IsRequired().HasDefaultValue(true);

            builder.Property(tr => tr.DueDate).HasColumnType("date");


            builder.Property(x => x.Visibility)
                .IsRequired()
                .HasConversion<int>();


            builder.Property(x => x.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.HasOne(x => x.Owner)
                .WithMany(x => x.OwnedTaskRequests)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);



        }
    }
}
