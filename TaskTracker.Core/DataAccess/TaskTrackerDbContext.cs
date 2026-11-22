using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Entities.Configurations;
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
        public DbSet<SubTask> SubTasks { get; set; }
        public DbSet<SubTaskSubmission> SubTaskSubmissions { get; set; }
        public DbSet<SubTaskSubmissionReview> SubTaskSubmissionReviews { get; set; }
        public DbSet<SubTaskActivity> SubTaskActivities { get; set; }
        public DbSet<TaskShare> TaskShares { get; set; }
        public DbSet<TaskShareInvitation> TaskShareInvitations { get; set; }
        public DbSet<TaskSubmission> TaskSubmissions { get; set; }
        public DbSet<TaskSubmissionReview> TaskSubmissionReviews { get; set; }
        public DbSet<TaskActivity> TaskActivities { get; set; }
        public DbSet<TaskPlanningRevision> TaskPlanningRevisions { get; set; }
        public DbSet<TaskMessage> TaskMessages { get; set; }
        public DbSet<TaskAttachment> TaskAttachments { get; set; }
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
                if (entry.Entity is TaskSubmission or TaskSubmissionReview or TaskActivity or TaskMessage or
                        TaskPlanningRevision or SubTaskSubmission or SubTaskSubmissionReview or SubTaskActivity &&
                    entry.State is EntityState.Modified or EntityState.Deleted)
                    throw new InvalidOperationException("Workflow history is immutable. Add a new revision or decision instead.");

                if (entry.Entity is TaskAttachment attachment &&
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                    ValidateAttachment(Entry(attachment));

                if (entry.Entity is WorkspaceActivity && entry.State is EntityState.Modified or EntityState.Deleted)
                    throw new InvalidOperationException("Workspace activity history is immutable.");

                if (entry.Entity is SubTask subTask && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                    ValidateSubTask(Entry(subTask));

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

                if (entry.Entity is TaskMessage message)
                    ValidateMessage(message);

                if (entry.Entity is TaskSubmission submission &&
                    (submission.RevisionNumber <= 0 || string.IsNullOrWhiteSpace(submission.Content) ||
                     submission.Content.Length > TaskSubmission.MaxContentLength))
                    throw new ValidationException("A submission requires a positive revision and 1–10000 characters of nonblank content.");

                if (entry.Entity is TaskSubmissionReview review &&
                    (!Enum.IsDefined(review.Decision) || review.Feedback?.Length > TaskSubmissionReview.MaxFeedbackLength ||
                     (review.Decision == TaskReviewDecision.ChangesRequested && string.IsNullOrWhiteSpace(review.Feedback))))
                    throw new ValidationException("A review requires a valid decision, feedback up to 5000 characters, and nonblank feedback when requesting changes.");

                if (entry.Entity is SubTaskSubmission subTaskSubmission &&
                    (subTaskSubmission.RevisionNumber <= 0 || string.IsNullOrWhiteSpace(subTaskSubmission.Content) ||
                     subTaskSubmission.Content.Length > SubTaskSubmission.MaxContentLength))
                    throw new ValidationException("A subtask submission requires a positive revision and nonblank content within the length limit.");

                if (entry.Entity is SubTaskSubmissionReview subTaskReview &&
                    (!Enum.IsDefined(subTaskReview.Decision) || subTaskReview.Feedback?.Length > SubTaskSubmissionReview.MaxFeedbackLength ||
                     (subTaskReview.Decision == TaskReviewDecision.ChangesRequested && string.IsNullOrWhiteSpace(subTaskReview.Feedback))))
                    throw new ValidationException("A subtask review requires a valid decision and feedback within the length limit. Requested changes require nonblank feedback.");

                if (entry.Entity is SubTaskActivity subTaskActivity)
                    ValidateSubTaskActivity(Entry(subTaskActivity));

                if (entry.Entity is TaskActivity activity &&
                    (!Enum.IsDefined(activity.ActivityType) ||
                     (activity.FromStatus.HasValue && !Enum.IsDefined(activity.FromStatus.Value)) ||
                     (activity.ToStatus.HasValue && !Enum.IsDefined(activity.ToStatus.Value)) ||
                     (activity.ActivityType == TaskActivityType.TaskDeleted &&
                      (activity.FromStatus.HasValue || activity.ToStatus.HasValue))))
                    throw new ValidationException("Activity type and optional statuses must be defined values.");
            }

            foreach (var entry in ChangeTracker.Entries().Where(x => x.State == EntityState.Modified &&
                         x.Entity is TaskRequest or SubTask or Workspace or WorkspaceMember or WorkspaceInvitation))
            {
                var version = entry.Property(nameof(TaskRequest.Version));
                version.CurrentValue = checked(Convert.ToInt64(version.OriginalValue) + 1);
                version.IsModified = true;
            }
        }

        private void ValidateMessage(TaskMessage message)
        {
            if (message.Content is null || message.Content.Length > TaskMessage.MaxContentLength ||
                (string.IsNullOrWhiteSpace(message.Content) && (message.Content.Length != 0 || message.DeliveryFileCount <= 0)))
                throw new ValidationException("A message requires text of up to 4000 characters or a delivered attachment.");

            if (message.ClientRequestId is null)
            {
                if (message.DeliveryFingerprint is not null || message.DeliveryFileCount != 0)
                    throw new ValidationException("A discussion delivery requires a request identifier.");
                return;
            }

            if (message.ClientRequestId == Guid.Empty || message.DeliveryFingerprint?.Length != 64 ||
                message.DeliveryFileCount is < 0 or > TaskMessage.MaxDeliveryFileCount)
                throw new ValidationException("Discussion delivery metadata is invalid.");

            var files = ChangeTracker.Entries<TaskAttachment>().Where(x => x.State == EntityState.Added &&
                ReferenceEquals(x.Entity.TaskMessage, message)).Select(x => x.Entity).ToArray();
            if (files.Length != message.DeliveryFileCount || files.Any(x => x.Kind != TaskAttachmentKind.Message ||
                x.TaskRequestId != message.TaskRequestId || x.UploaderUserId != message.SenderUserId || x.IsDeleted))
                throw new ValidationException("All discussion delivery files must be saved with their message.");
        }

        private static void ValidateAttachment(EntityEntry<TaskAttachment> entry)
        {
            var attachment = entry.Entity;
            if (entry.State is EntityState.Modified or EntityState.Deleted &&
                (entry.Property(x => x.Kind).OriginalValue != TaskAttachmentKind.Task || attachment.Kind != TaskAttachmentKind.Task))
                throw new InvalidOperationException("Discussion and submission attachments are immutable workflow evidence.");

            if (entry.State == EntityState.Deleted)
                return;

            // Read tracked values so a new submission and its evidence can share EF's temporary keys.
            var hasMessage = entry.Property(x => x.TaskMessageId).CurrentValue.HasValue;
            var hasSubmission = entry.Property(x => x.TaskSubmissionId).CurrentValue.HasValue;
            var hasSubTaskSubmission = entry.Property(x => x.SubTaskSubmissionId).CurrentValue.HasValue;
            var validContext = attachment.Kind switch
            {
                TaskAttachmentKind.Task => !hasMessage && !hasSubmission && !hasSubTaskSubmission,
                TaskAttachmentKind.Message => hasMessage && !hasSubmission && !hasSubTaskSubmission,
                TaskAttachmentKind.Submission => !hasMessage && hasSubmission && !hasSubTaskSubmission,
                TaskAttachmentKind.SubTaskSubmission => !hasMessage && !hasSubmission && hasSubTaskSubmission &&
                    !attachment.IsDeleted && !attachment.DeletedAt.HasValue && !attachment.DeletedByUserId.HasValue,
                _ => false
            };

            if (!validContext || attachment.SizeBytes <= 0 ||
                string.IsNullOrWhiteSpace(attachment.OriginalFileName) || attachment.OriginalFileName.Length > 255 ||
                string.IsNullOrWhiteSpace(attachment.StorageKey) || attachment.StorageKey.Length > 100 ||
                string.IsNullOrWhiteSpace(attachment.ContentType) || attachment.ContentType.Length > 100)
                throw new ValidationException("An attachment requires valid metadata and exactly one matching context.");
        }

        private static void ValidateSubTask(EntityEntry<SubTask> entry)
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Record subtask deletion using its deletion audit fields.");

            var subTask = entry.Entity;
            if (string.IsNullOrWhiteSpace(subTask.Title) || subTask.Title.Length > SubTask.MaxTitleLength ||
                string.IsNullOrWhiteSpace(subTask.Description) || subTask.Description.Length > SubTask.MaxDescriptionLength ||
                !Enum.IsDefined(subTask.Priority) || !Enum.IsDefined(subTask.Status))
                throw new ValidationException("A subtask requires a nonblank title and description within their length limits, a valid priority and a valid status.");

            var deleted = !subTask.Activity;
            if (subTask.DeletedAt.HasValue != deleted || entry.Property(x => x.DeletedByUserId).CurrentValue.HasValue != deleted ||
                subTask.DeletedAt < subTask.CreatedAt)
                throw new ValidationException("Deletion requires both a timestamp and an actor. An active subtask must have neither.");

            if (entry.State == EntityState.Modified && !entry.Property(x => x.Activity).OriginalValue &&
                (subTask.Activity || entry.Property(x => x.DeletedAt).IsModified || entry.Property(x => x.DeletedByUserId).IsModified))
                throw new InvalidOperationException("A recorded subtask deletion is immutable.");
        }

        private static void ValidateSubTaskActivity(EntityEntry<SubTaskActivity> entry)
        {
            var activity = entry.Entity;
            var type = activity.ActivityType;
            var assignment = type is TaskActivityType.UserAssigned or TaskActivityType.UserUnassigned;
            var review = type is TaskActivityType.ChangesRequested or TaskActivityType.SubmissionApproved;
            var hasTarget = entry.Property(x => x.TargetUserId).CurrentValue.HasValue;
            var hasSubmission = entry.Property(x => x.SubmissionId).CurrentValue.HasValue;
            var hasReview = entry.Property(x => x.ReviewId).CurrentValue.HasValue;

            if (!SubTaskActivity.SupportedTypes.Contains(type) ||
                (activity.FromStatus.HasValue && !Enum.IsDefined(activity.FromStatus.Value)) ||
                (activity.ToStatus.HasValue && !Enum.IsDefined(activity.ToStatus.Value)) ||
                (type == TaskActivityType.TaskDeleted && (activity.FromStatus.HasValue || activity.ToStatus.HasValue)) ||
                hasTarget != assignment || hasReview != review || hasSubmission != (type == TaskActivityType.SubmissionCreated || review))
                throw new ValidationException("The subtask activity type, statuses and linked records must match the operation.");
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            SubTaskCheckSql.ConfigureNonBlankChecks(builder, Database.IsNpgsql());
            base.OnModelCreating(builder);
        }






    }
}
