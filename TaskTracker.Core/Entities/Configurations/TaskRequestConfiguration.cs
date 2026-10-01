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
            builder.ToTable("TaskRequests", table => table.HasCheckConstraint(
                "CK_TaskRequests_DeletionAudit",
                "(\"Activity\" = TRUE AND \"DeletedAt\" IS NULL AND \"DeletedByUserId\" IS NULL) OR " +
                "(\"Activity\" = FALSE AND ((\"DeletedAt\" IS NULL AND \"DeletedByUserId\" IS NULL) OR " +
                "(\"DeletedAt\" IS NOT NULL AND \"DeletedByUserId\" IS NOT NULL)))"));

            builder.HasKey(tr => tr.Id);
            builder.Property(tr => tr.Id).ValueGeneratedOnAdd();

            builder.Property(tr => tr.Title).IsRequired().HasMaxLength(TaskRequest.MaxTitleLength);

            builder.Property(tr => tr.Description).IsRequired().HasMaxLength(TaskRequest.MaxDescriptionLength);

            builder.Property(tr => tr.Category).IsRequired().HasMaxLength(TaskRequest.MaxCategoryLength);

            builder.Property(tr => tr.Priority).IsRequired().HasConversion<string>().HasMaxLength(50);

            builder.Property(tr => tr.Status).IsRequired().HasMaxLength(50).HasConversion<string>().HasMaxLength(50);

            builder.Property(tr => tr.Activity).IsRequired().HasDefaultValue(true);
            builder.Property(tr => tr.Version).IsConcurrencyToken().HasDefaultValue(0L);

            builder.Property(tr => tr.DueDate).HasColumnType("date");

            builder.Property(x => x.Visibility).IsRequired().HasConversion<int>();

            builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.HasOne(x => x.Owner).WithMany(x => x.OwnedTaskRequests).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Workspace).WithMany(x => x.Tasks).HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(x => x.WorkspaceId);

        }
    }
}
