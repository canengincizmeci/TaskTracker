using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.DataAccess.Concrete.EfCore;

public class EfSubTaskDal(TaskTrackerDbContext context) : ISubTaskDal
{
    public Task LockParentAsync(int taskId, bool exclusive, CancellationToken ct = default) => exclusive
        ? LockAsync($"""SELECT "Id" FROM "TaskRequests" WHERE "Id" = {taskId} FOR NO KEY UPDATE""", ct)
        : LockAsync($"""SELECT "Id" FROM "TaskRequests" WHERE "Id" = {taskId} FOR SHARE""", ct);

    public Task LockParentWorkspaceAsync(int taskId, CancellationToken ct = default) => LockAsync($"""
        SELECT w."Id" FROM "Workspaces" w
        JOIN "TaskRequests" t ON t."WorkspaceId" = w."Id"
        WHERE t."Id" = {taskId} FOR SHARE OF w
        """, ct);

    public Task LockWorkspaceForMemberRemovalAsync(int workspaceId, CancellationToken ct = default) =>
        LockAsync($"""SELECT "Id" FROM "Workspaces" WHERE "Id" = {workspaceId} FOR NO KEY UPDATE""", ct);

    public Task LockSubTaskForReadAsync(int taskId, int subTaskId, CancellationToken ct = default) => LockAsync($"""
        SELECT "Id" FROM "SubTasks" WHERE "TaskRequestId" = {taskId} AND "Id" = {subTaskId} FOR SHARE
        """, ct);

    private Task LockAsync(FormattableString sql, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Subtask workflow locks require a transaction.");

        // SQLite's default write transaction already serializes writers for the database.
        return context.Database.IsNpgsql() ? context.Database.ExecuteSqlInterpolatedAsync(sql, ct) : Task.CompletedTask;
    }

    private IQueryable<SubTask> Active(int taskId) =>
        context.SubTasks.Where(x => x.TaskRequestId == taskId && x.Activity);

    public Task<TaskRequest?> GetActiveParentAsync(int taskId, CancellationToken ct = default) =>
        context.TaskRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == taskId && x.Activity, ct);

    private IQueryable<SubTask> Open() => context.SubTasks.Where(x => x.Activity &&
        (x.Status == TaskStatus.Pending || x.Status == TaskStatus.InProgress || x.Status == TaskStatus.InReview));

    public Task<SubTask?> GetActiveAsync(int taskId, int subTaskId, CancellationToken ct = default) =>
        Active(taskId).SingleOrDefaultAsync(x => x.Id == subTaskId, ct);

    public Task<bool> HasActiveAsync(int taskId, int subTaskId, CancellationToken ct = default) =>
        Active(taskId).AnyAsync(x => x.Id == subTaskId, ct);

    private IQueryable<SubTaskDto> Details(IQueryable<SubTask> query, int userId) =>
        from x in query
        let parent = x.TaskRequest
        let unlocked = parent.Status == TaskStatus.Pending || parent.Status == TaskStatus.InProgress
        let owner = unlocked && parent.OwnerId == userId
        let editable = x.Status == TaskStatus.Pending || x.Status == TaskStatus.InProgress
        let delegated = x.AssigneeUserId != null && x.AssigneeUserId != parent.OwnerId
        let latestId = context.SubTaskSubmissions.Where(s => s.SubTaskId == x.Id && s.TaskRequestId == x.TaskRequestId)
            .OrderByDescending(s => s.RevisionNumber).Select(s => (int?)s.Id).FirstOrDefault()
        let eligible = unlocked && editable && (x.AssigneeUserId ?? parent.OwnerId) == userId &&
            (parent.WorkspaceId == null ? parent.OwnerId == userId || parent.TaskShares.Any(s =>
                s.SharedWithUserId == userId && s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage)
                : parent.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive))
        let reviewable = owner && x.Status == TaskStatus.InReview && delegated &&
            (parent.WorkspaceId == null ? parent.TaskShares.Any(s => s.SharedWithUserId == x.AssigneeUserId &&
                s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage)
                : parent.Workspace!.Members.Any(m => m.UserId == x.AssigneeUserId && m.IsActive)) &&
            latestId != null && context.SubTaskSubmissions.Any(s => s.Id == latestId && s.SubmittedByUserId == x.AssigneeUserId &&
                !context.SubTaskSubmissionReviews.Any(r => r.SubTaskSubmissionId == s.Id))
        select new SubTaskDto(x.Id, x.TaskRequestId, x.CreatedByUserId, x.AssigneeUserId,
            x.Version, x.Title, x.Description, x.Priority, x.Status, x.DueDate, x.CreatedAt,
            x.Status == TaskStatus.InReview ? latestId : null)
        {
            AssigneeUserName = x.Assignee == null ? null : x.Assignee.UserName,
            Capabilities = new SubTaskCapabilitiesDto(owner && editable, owner, owner && editable,
                eligible && x.Status == TaskStatus.Pending, owner && editable && !delegated,
                eligible && delegated && x.Status == TaskStatus.InProgress, reviewable,
                owner && (editable || x.Status == TaskStatus.InReview),
                owner && (x.Status == TaskStatus.Completed || x.Status == TaskStatus.Cancelled))
        };

    public Task<SubTaskDto> GetDetailAsync(int taskId, int subTaskId, int userId, CancellationToken ct = default) =>
        Details(Active(taskId).Where(x => x.Id == subTaskId), userId).SingleAsync(ct);

    public Task<List<SubTaskDto>> ListAsync(int taskId, int userId, CancellationToken ct = default) =>
        Details(Active(taskId).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id), userId).ToListAsync(ct);

    public Task<List<AssignedSubTaskDto>> ListAssignedAsync(int userId) => OpenAssignments(userId)
        .Where(x => x.TaskRequest.Status != TaskStatus.Completed && x.TaskRequest.Status != TaskStatus.Cancelled &&
            (x.TaskRequest.WorkspaceId != null
                ? x.TaskRequest.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive)
                : x.TaskRequest.OwnerId == userId || x.TaskRequest.TaskShares.Any(s =>
                    s.SharedWithUserId == userId && s.Permission >= TaskPermission.Edit &&
                    s.Permission <= TaskPermission.Manage)))
        .OrderBy(x => x.DueDate == null).ThenBy(x => x.DueDate).ThenBy(x => x.Id)
        .Select(x => new AssignedSubTaskDto(x.Id, x.TaskRequestId, x.TaskRequest.Title, x.TaskRequest.Status,
            x.Title, x.Priority, x.Status, x.DueDate)).ToListAsync();

    public Task<bool> HasOpenAsync(int taskId) => Open().AnyAsync(x => x.TaskRequestId == taskId);

    private IQueryable<SubTask> OpenAssignments(int userId) => Open()
        .Where(x => x.AssigneeUserId == userId && x.TaskRequest.Activity).OrderBy(x => x.Id);

    public Task<List<SubTask>> GetOpenTaskAssignmentsAsync(int taskId, int userId) =>
        OpenAssignments(userId).Where(x => x.TaskRequestId == taskId).ToListAsync();

    public Task<List<SubTask>> GetOpenWorkspaceAssignmentsAsync(int workspaceId, int userId) =>
        OpenAssignments(userId).Where(x => x.TaskRequest.WorkspaceId == workspaceId).ToListAsync();

    private IQueryable<SubTaskSubmission> Submissions(int taskId, int subTaskId) =>
        context.SubTaskSubmissions.Where(x => x.TaskRequestId == taskId && x.SubTaskId == subTaskId)
            .OrderByDescending(x => x.RevisionNumber);

    public Task<SubTaskSubmission?> GetLatestSubmissionAsync(int taskId, int subTaskId) =>
        Submissions(taskId, subTaskId).FirstOrDefaultAsync();

    public Task<List<SubTaskSubmissionDto>> GetSubmissionsAsync(int taskId, int subTaskId, CancellationToken ct = default) =>
        (from submission in Submissions(taskId, subTaskId)
         join review in context.SubTaskSubmissionReviews on submission.Id equals review.SubTaskSubmissionId into reviews
         from review in reviews.DefaultIfEmpty()
         orderby submission.RevisionNumber descending
         select new SubTaskSubmissionDto(submission.Id, submission.RevisionNumber, submission.SubmittedByUserId,
             submission.Content, submission.CreatedAt, review == null ? null :
                 new SubTaskReviewDto(review.Id, review.ReviewerUserId, review.Decision, review.Feedback, review.CreatedAt)
                 { ReviewerUserName = review.ReviewerUser.UserName })
         {
             SubmittedByUserName = submission.SubmittedByUser.UserName,
             DeliveryFileCount = submission.DeliveryFileCount
         })
        .ToListAsync(ct);

    public Task<List<SubTaskActivityDto>> GetActivitiesAsync(int taskId, int subTaskId, CancellationToken ct = default) => context.SubTaskActivities
        .Where(x => x.TaskRequestId == taskId && x.SubTaskId == subTaskId)
        .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
        .Select(x => new SubTaskActivityDto(x.Id, x.ActorUserId, x.ActivityType, x.CreatedAt,
            x.TargetUserId, x.SubmissionId, x.ReviewId, x.FromStatus, x.ToStatus)
        { ActorUserName = x.ActorUser.UserName, TargetUserName = x.TargetUser == null ? null : x.TargetUser.UserName })
        .ToListAsync(ct);
}
