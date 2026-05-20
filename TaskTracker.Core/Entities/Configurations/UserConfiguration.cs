using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("Users");

            builder.HasKey(u => u.Id);
            builder.Property(u => u.Id).ValueGeneratedOnAdd();

            builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);

            builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);

            builder.Property(u => u.Email).IsRequired().HasMaxLength(200);

            //builder.HasIndex(u => u.Email).IsUnique();

            builder.Property(u => u.PasswordSalt).IsRequired();

            builder.Property(u => u.PasswordHash).IsRequired();

            builder.Property(u => u.Status).IsRequired();

            builder.HasMany(u => u.UserOperationClaims).WithOne(uoc => uoc.User).HasForeignKey(uoc => uoc.UserId);
        }
    }
}
