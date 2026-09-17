using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using System.ComponentModel.DataAnnotations;

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
        public DbSet<TaskSubmission> TaskSubmissions { get; set; }
        public DbSet<TaskSubmissionReview> TaskSubmissionReviews { get; set; }
        public DbSet<TaskActivity> TaskActivities { get; set; }
        public DbSet<TaskMessage> TaskMessages { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<PasswordResetRequest> PasswordResetRequests { get; set; }

         

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            PrepareWorkflowChanges();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            PrepareWorkflowChanges();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void PrepareWorkflowChanges()
        {
            ChangeTracker.DetectChanges();

            // All repository writes pass through this context. Bulk SQL/ExecuteUpdate
            // bypass these guards and must not be used for workflow history.
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.Entity is TaskSubmission or TaskSubmissionReview or TaskActivity or TaskMessage &&
                    entry.State is EntityState.Modified or EntityState.Deleted)
                    throw new InvalidOperationException("Workflow history is immutable. Add a new revision or decision instead.");

                if (entry.State != EntityState.Added)
                    continue;

                if (entry.Entity is TaskMessage message &&
                    (string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > TaskMessage.MaxContentLength))
                    throw new ValidationException("A message requires 1–4000 characters of nonblank content.");

                if (entry.Entity is TaskSubmission submission &&
                    (submission.RevisionNumber <= 0 || string.IsNullOrWhiteSpace(submission.Content) ||
                     submission.Content.Length > TaskSubmission.MaxContentLength))
                    throw new ValidationException("A submission requires a positive revision and 1–10000 characters of nonblank content.");

                if (entry.Entity is TaskSubmissionReview review &&
                    (!Enum.IsDefined(review.Decision) || review.Feedback?.Length > TaskSubmissionReview.MaxFeedbackLength ||
                     (review.Decision == TaskReviewDecision.ChangesRequested && string.IsNullOrWhiteSpace(review.Feedback))))
                    throw new ValidationException("A review requires a valid decision, feedback up to 5000 characters, and nonblank feedback when requesting changes.");

                if (entry.Entity is TaskActivity activity &&
                    (!Enum.IsDefined(activity.ActivityType) ||
                     (activity.FromStatus.HasValue && !Enum.IsDefined(activity.FromStatus.Value)) ||
                     (activity.ToStatus.HasValue && !Enum.IsDefined(activity.ToStatus.Value))))
                    throw new ValidationException("Activity type and optional statuses must be defined values.");
            }

            foreach (var entry in ChangeTracker.Entries<TaskRequest>().Where(x => x.State == EntityState.Modified))
            {
                var version = entry.Property(x => x.Version);
                version.CurrentValue = checked(version.OriginalValue + 1);
                version.IsModified = true;
            }
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            base.OnModelCreating(builder);
        }






    }
}
