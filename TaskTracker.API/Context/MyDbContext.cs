using Microsoft.EntityFrameworkCore;
using System.Reflection;
using TaskTracker.API.Entitites;

namespace TaskTracker.API.Context
{
    public class MyDbContext : DbContext
    {
        public MyDbContext(DbContextOptions<MyDbContext> options) : base(options)
        {

        }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<AdminOtp> AdminOtps { get; set; }
        public DbSet<TaskRequest> TaskRequests { get; set; }
        public DbSet<AdminSession> AdminSessions { get; set; }


        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            base.OnModelCreating(builder);
        }


    }
}
