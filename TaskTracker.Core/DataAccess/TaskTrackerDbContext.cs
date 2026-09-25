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
        public DbSet<Workspace> Workspaces { get; set; }
        public DbSet<WorkspaceMember> WorkspaceMembers { get; set; }
        public DbSet<WorkspaceInvitation> WorkspaceInvitations { get; set; }
        public DbSet<WorkspaceActivity> WorkspaceActivities { get; set; }

         

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

                if (entry.Entity is WorkspaceActivity && entry.State is EntityState.Modified or EntityState.Deleted)
                    throw new InvalidOperationException("Workspace activity history is immutable.");

                if (entry.Entity is Workspace workspace && entry.State is EntityState.Added or EntityState.Modified)
                {
                    workspace.Name = workspace.Name?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(workspace.Name) || workspace.Name.Length > Workspace.MaxNameLength)
                        throw new ValidationException($"A workspace name requires 1–{Workspace.MaxNameLength} nonblank characters.");
                }

                if (entry.Entity is WorkspaceMember member && entry.State is EntityState.Added or EntityState.Modified &&
                    (!Enum.IsDefined(member.Role) || member.IsActive != !member.RemovedAt.HasValue))
                    throw new ValidationException("A workspace membership requires a valid role and consistent removal state.");

                if (entry.Entity is WorkspaceInvitation invitation && entry.State is EntityState.Added or EntityState.Modified &&
                    !Enum.IsDefined(invitation.Status))
                    throw new ValidationException("A workspace invitation requires a valid status.");

                if (entry.State != EntityState.Added)
                    continue;

                if (entry.Entity is WorkspaceActivity workspaceActivity &&
                    (!Enum.IsDefined(workspaceActivity.ActivityType) ||
                     (workspaceActivity.FromRole.HasValue && !Enum.IsDefined(workspaceActivity.FromRole.Value)) ||
                     (workspaceActivity.ToRole.HasValue && !Enum.IsDefined(workspaceActivity.ToRole.Value))))
                    throw new ValidationException("Workspace activity type and optional roles must be defined values.");

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

            foreach (var entry in ChangeTracker.Entries().Where(x => x.State == EntityState.Modified &&
                         x.Entity is TaskRequest or Workspace or WorkspaceMember or WorkspaceInvitation))
            {
                var version = entry.Property(nameof(TaskRequest.Version));
                version.CurrentValue = checked(Convert.ToInt64(version.OriginalValue) + 1);
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
