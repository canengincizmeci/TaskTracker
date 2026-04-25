using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.API.Entitites;

namespace TaskTracker.API.EntityConfigurations
{
    public class AdminConfiguraiton : IEntityTypeConfiguration<Admin>
    {
        public void Configure(EntityTypeBuilder<Admin> builder)
        {
            builder.ToTable("Admins");

            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).ValueGeneratedOnAdd();

            builder.Property(a => a.Username).IsRequired().HasMaxLength(100);

            builder.Property(a=>a.Email).IsRequired().HasMaxLength(100);    

            builder.Property(a=>a.FullName).IsRequired().HasMaxLength(100);
            builder.Property(a => a.PasswordHash)
       .IsRequired()
       .HasMaxLength(500);
        }
    }
}
