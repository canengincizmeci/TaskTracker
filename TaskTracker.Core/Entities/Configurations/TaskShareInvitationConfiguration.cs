using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations
{
    public class TaskShareInvitationConfiguration : IEntityTypeConfiguration<TaskShareInvitation>
    {
        public void Configure(EntityTypeBuilder<TaskShareInvitation> builder)
        {
            builder.HasKey(t => t.Id);

            builder.Property(t => t.Id).ValueGeneratedOnAdd();

            builder.Property(t => t.TaskRequestId).IsRequired();

            builder.Property(t => t.InvitedByUserId).IsRequired();

            builder.Property(x => x.Permission).IsRequired().HasConversion<int>();

            builder.Property(x => x.Status).IsRequired().HasConversion<int>();

            builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(x => x.RespondedAt).IsRequired(false);

            builder.Property(x => x.ExpiresAt).IsRequired(false);

            builder.HasOne(x => x.TaskRequest).WithMany().HasForeignKey(x => x.TaskRequestId).OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.InvitedUser).WithMany().HasForeignKey(x => x.InvitedUserId).OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.InvitedByUser).WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => new
            {
                x.TaskRequestId,
                x.InvitedUserId,
                x.Status
            });
        }
    }
}



