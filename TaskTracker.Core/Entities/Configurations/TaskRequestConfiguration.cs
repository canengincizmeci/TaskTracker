using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations
{
    public class TaskRequestConfiguration : IEntityTypeConfiguration<TaskRequest>
    {
        public void Configure(EntityTypeBuilder<TaskRequest> builder)
        {
            builder.ToTable("TaskRequests");

            builder.HasKey(tr => tr.Id);
            builder.Property(tr => tr.Id).ValueGeneratedOnAdd();

            builder.Property(tr => tr.Title).IsRequired().HasMaxLength(50);

            builder.Property(tr => tr.Description).IsRequired().HasMaxLength(10000);

            builder.Property(tr => tr.Category).IsRequired().HasMaxLength(50);

            builder.Property(tr => tr.Priority).IsRequired();

            builder.Property(tr => tr.Status).IsRequired();

            builder.Property(tr => tr.CreatedAt).IsRequired();






        }
    }
}
