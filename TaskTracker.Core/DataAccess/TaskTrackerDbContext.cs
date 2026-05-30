using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.DataAccess
{
    public class TaskTrackerDbContext : DbContext
    {
        public TaskTrackerDbContext(DbContextOptions<TaskTrackerDbContext> options) : base(options)
        {

        }
        public DbSet<User> Users { get; set; }
        public DbSet<OperationClaim> OperationClaims { get; set; }
        public DbSet<UserOperationClaim> UserOperationClaims { get; set; }
        public DbSet<EmailVerification> EmailVerifications { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<TaskRequest> TaskRequests { get; set; }
        public DbSet<TaskShare> TaskShares { get; set; }
        public DbSet<TaskShareInvitation> TaskShareInvitations { get; set; }
        public DbSet<Notification> Notifications { get; set; }

         

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            base.OnModelCreating(builder);
        }






    }
}
