using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Utilities.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.Repository;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.DataAccess.Concrete.EfCore
{
    public class EfTaskRequestDal : EfEntityRepositoryBase<TaskRequest, TaskTrackerDbContext>, ITaskRequestDal
    {
        public EfTaskRequestDal(TaskTrackerDbContext context) : base(context)
        {

        }
        public Task<bool> CanEditAsync(int taskId, int userId)
            => _context.TaskRequests.AnyAsync(x => x.Id == taskId && x.Activity &&
                (x.WorkspaceId == null && (x.OwnerId == userId || x.TaskShares.Any(s =>
                     s.SharedWithUserId == userId && s.Permission >= TaskPermission.Edit &&
                     s.Permission <= TaskPermission.Manage)) ||
                 x.WorkspaceId != null && x.OwnerId == userId && x.Workspace!.Members.Any(m =>
                     m.UserId == userId && m.IsActive)));

        public Task<bool> CanManageAsync(int taskId, int userId)
            => _context.TaskRequests.AnyAsync(x => x.Id == taskId && x.Activity && x.OwnerId == userId &&
                (x.WorkspaceId == null || x.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive)));

        public Task<bool> CanViewAsync(int taskId, int userId)
            => _context.TaskRequests.AnyAsync(x => x.Id == taskId && x.Activity &&
                (x.WorkspaceId == null && (x.OwnerId == userId || x.Visibility == TaskVisibility.Public ||
                     x.TaskShares.Any(s => s.SharedWithUserId == userId &&
                         s.Permission >= TaskPermission.View && s.Permission <= TaskPermission.Manage)) ||
                 x.WorkspaceId != null && x.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive)));

        public async Task<List<TaskRequest>> GetTasksByUserIdAsync(int userId)
        {
            var tasks =await _context.TaskRequests.AsNoTracking().Include(t => t.Owner).Include(t => t.Assignee)
                .Include(t => t.Workspace).Include(t => t.TaskShares).Where(t => t.Activity &&
                    (t.WorkspaceId == null && (t.OwnerId == userId || t.TaskShares.Any(ts =>
                         ts.SharedWithUserId == userId && ts.Permission >= TaskPermission.View &&
                         ts.Permission <= TaskPermission.Manage)) ||
                     t.WorkspaceId != null && t.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive)))
                .OrderByDescending(t => t.CreatedAt).ToListAsync();
              
            return tasks;
        }  

        public Task<List<GetTasksDto>> GetWorkspaceTasksAsync(int workspaceId, int userId) =>
            _context.TaskRequests.AsNoTracking()
                .Where(x => x.Activity && x.WorkspaceId == workspaceId)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Select(x => new GetTasksDto
                {
                    Id = x.Id,
                    OwnerId = x.OwnerId,
                    OwnerUserName = x.Owner.UserName,
                    WorkspaceId = x.WorkspaceId,
                    WorkspaceName = x.Workspace!.Name,
                    AssigneeUserId = x.AssigneeUserId,
                    AssigneeUserName = x.Assignee == null ? null : x.Assignee.UserName,
                    IsOwner = x.OwnerId == userId,
                    Title = x.Title,
                    Description = x.Description,
                    Category = x.Category,
                    Priority = x.Priority.ToString(),
                    Status = x.Status.ToString(),
                    Activity = x.Activity,
                    SharedCount = x.SharedCount,
                    DueDate = x.DueDate,
                    Visibility = x.Visibility.ToString(),
                    CreatedAt = x.CreatedAt,
                    IsSharedWithMe = false,
                    CanView = true,
                    CanEdit = x.OwnerId == userId,
                    CanShare = false,
                    Version = x.Version
                }).ToListAsync();

        public Task<List<TaskRequest>> GetAssignedTasksAsync(int userId) => _context.TaskRequests.AsNoTracking()
            .Include(t => t.Owner).Include(t => t.Assignee).Include(t => t.Workspace).Include(t => t.TaskShares)
            .Where(t => t.Activity && t.AssigneeUserId == userId &&
                (t.WorkspaceId == null || t.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive)))
            .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        public async Task<int> GetLatestRevisionNumberAsync(int taskId) =>
            await _context.TaskSubmissions.Where(x => x.TaskRequestId == taskId)
                .MaxAsync(x => (int?)x.RevisionNumber) ?? 0;

        public async Task<int> GetLatestPlanningRevisionNumberAsync(int taskId) =>
            await _context.TaskPlanningRevisions.Where(x => x.TaskRequestId == taskId)
                .MaxAsync(x => (int?)x.RevisionNumber) ?? 0;

        public Task<List<TaskPlanningRevision>> GetPlanningRevisionsAsync(int taskId) =>
            _context.TaskPlanningRevisions.AsNoTracking().Where(x => x.TaskRequestId == taskId)
                .OrderBy(x => x.RevisionNumber).ToListAsync();

        public Task<TaskSubmission?> GetLatestSubmissionAsync(int taskId) =>
            _context.TaskSubmissions.Include(x => x.SubmittedByUser)
                .Where(x => x.TaskRequestId == taskId)
                .OrderByDescending(x => x.RevisionNumber).FirstOrDefaultAsync();

        public Task<int?> GetSubmissionForLatestTransitionAsync(int taskId, TaskActivityType transition) =>
            _context.TaskActivities.Where(x => x.TaskRequestId == taskId && x.ToStatus != null)
                .OrderByDescending(x => x.Id)
                .Select(x => x.ActivityType == transition ? x.SubmissionId : null)
                .FirstOrDefaultAsync();

        public Task<List<TaskSubmissionDto>> GetSubmissionHistoryAsync(int taskId) =>
            _context.TaskSubmissions.AsNoTracking().Where(x => x.TaskRequestId == taskId)
                .OrderBy(x => x.RevisionNumber)
                .Select(x => new TaskSubmissionDto
                {
                    Id = x.Id, RevisionNumber = x.RevisionNumber,
                    SubmittedByUserId = x.SubmittedByUserId,
                    SubmittedByUserName = x.SubmittedByUser.UserName,
                    Content = x.Content, CreatedAt = x.CreatedAt,
                    Review = _context.TaskSubmissionReviews.Where(r => r.TaskSubmissionId == x.Id)
                        .Select(r => new TaskSubmissionReviewDto
                        {
                            Id = r.Id, ReviewerUserId = r.ReviewerUserId,
                            ReviewerUserName = r.ReviewerUser.UserName,
                            Decision = r.Decision.ToString(), Feedback = r.Feedback,
                            CreatedAt = r.CreatedAt
                        }).SingleOrDefault()
                }).ToListAsync();

        public Task<List<AwaitingReviewTaskDto>> GetAwaitingReviewAsync(int ownerId) =>
            _context.TaskRequests.AsNoTracking()
                .Where(x => x.Activity && x.OwnerId == ownerId && x.Status == TaskStatus.InReview)
                .Select(x => new AwaitingReviewTaskDto
                {
                    TaskId = x.Id, Title = x.Title, Version = x.Version,
                    AssigneeUserId = x.AssigneeUserId, AssigneeUserName = x.Assignee != null ? x.Assignee.UserName : null,
                    DueDate = x.DueDate,
                    LatestSubmissionId = _context.TaskSubmissions.Where(s => s.TaskRequestId == x.Id)
                        .OrderByDescending(s => s.RevisionNumber).Select(s => (int?)s.Id).FirstOrDefault(),
                    LatestRevisionNumber = _context.TaskSubmissions.Where(s => s.TaskRequestId == x.Id)
                        .OrderByDescending(s => s.RevisionNumber).Select(s => (int?)s.RevisionNumber).FirstOrDefault(),
                    SubmittedAt = _context.TaskSubmissions.Where(s => s.TaskRequestId == x.Id)
                        .OrderByDescending(s => s.RevisionNumber).Select(s => (DateTime?)s.CreatedAt).FirstOrDefault()
                })
                .OrderBy(x => x.SubmittedAt == null).ThenBy(x => x.SubmittedAt).ThenBy(x => x.TaskId)
                .ToListAsync();

        public async Task<WorkDashboardSummaryDto> GetWorkDashboardSummaryAsync(
            int userId, DateOnly todayUtc, DateTime nowUtc)
        {
            var assignedToMeCount = await _context.TaskRequests.AsNoTracking()
                .CountAsync(x => x.Activity && x.AssigneeUserId == userId &&
                    x.Status != TaskStatus.Completed && x.Status != TaskStatus.Cancelled &&
                    (x.WorkspaceId == null || x.Workspace!.Members.Any(m =>
                        m.UserId == userId && m.IsActive)));

            var awaitingMyReviewCount = await _context.TaskRequests.AsNoTracking()
                .Where(x => x.Activity && x.OwnerId == userId && x.Status == TaskStatus.InReview &&
                    x.AssigneeUserId.HasValue && x.AssigneeUserId != x.OwnerId &&
                    (x.WorkspaceId == null && x.TaskShares.Any(s =>
                         s.SharedWithUserId == x.AssigneeUserId.Value &&
                         s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage) ||
                     x.WorkspaceId != null && x.Workspace!.Members.Any(m =>
                         m.UserId == userId && m.IsActive) && x.Workspace.Members.Any(m =>
                         m.UserId == x.AssigneeUserId.Value && m.IsActive)))
                .CountAsync(x => _context.TaskSubmissions
                    .Where(s => s.TaskRequestId == x.Id)
                    .OrderByDescending(s => s.RevisionNumber)
                    .Take(1)
                    .Any(s => s.SubmittedByUserId == x.AssigneeUserId &&
                        !_context.TaskSubmissionReviews.Any(r => r.TaskSubmissionId == s.Id)));

            var overdueCount = await _context.TaskRequests.AsNoTracking()
                .CountAsync(x => x.Activity && x.DueDate.HasValue && x.DueDate.Value < todayUtc &&
                    x.Status != TaskStatus.Completed && x.Status != TaskStatus.Cancelled &&
                    (x.OwnerId == userId || x.AssigneeUserId == userId) &&
                    (x.WorkspaceId == null || x.Workspace!.Members.Any(m =>
                        m.UserId == userId && m.IsActive)));

            var pendingTaskInvitationsCount = await _context.TaskShareInvitations.AsNoTracking()
                .CountAsync(x => x.InvitedUserId == userId && x.Status == TaskShareInvitationStatus.Pending &&
                    (!x.ExpiresAt.HasValue || x.ExpiresAt > nowUtc) && x.TaskRequest.Activity &&
                    x.TaskRequest.Status != TaskStatus.Completed && x.TaskRequest.Status != TaskStatus.Cancelled);

            var pendingWorkspaceInvitationsCount = await _context.WorkspaceInvitations.AsNoTracking()
                .CountAsync(x => x.InvitedUserId == userId && x.Status == WorkspaceInvitationStatus.Pending &&
                    (!x.ExpiresAt.HasValue || x.ExpiresAt > nowUtc));

            return new WorkDashboardSummaryDto
            {
                AssignedToMeCount = assignedToMeCount + await DashboardSubTasks(userId).CountAsync(x => x.IsAssigned &&
                    x.Status != TaskStatus.Completed && x.Status != TaskStatus.Cancelled),
                AwaitingMyReviewCount = awaitingMyReviewCount + await DashboardSubTasks(userId).CountAsync(x =>
                    x.IsOwned && !x.ParentLocked && x.Status == TaskStatus.InReview && x.AssigneeCanWork &&
                    x.AssigneeUserId != x.OwnerId && x.LatestSubmissionAuthorId == x.AssigneeUserId &&
                    x.LatestReviewDecision == null),
                OverdueCount = overdueCount + await DashboardSubTasks(userId).CountAsync(x => x.DueDate < todayUtc &&
                    x.Status != TaskStatus.Completed && x.Status != TaskStatus.Cancelled),
                PendingTaskInvitationsCount = pendingTaskInvitationsCount,
                PendingWorkspaceInvitationsCount = pendingWorkspaceInvitationsCount
            };
        }

        public async Task<PagedWorkTasksDto> GetWorkTasksAsync(
            int userId, WorkTaskQueryDto query, DateOnly todayUtc)
        {
            var tasks = _context.TaskRequests.AsNoTracking().Where(x => x.Activity &&
                (x.WorkspaceId == null && (x.OwnerId == userId || x.AssigneeUserId == userId ||
                     x.TaskShares.Any(s => s.SharedWithUserId == userId &&
                         s.Permission >= TaskPermission.View && s.Permission <= TaskPermission.Manage)) ||
                 x.WorkspaceId != null && x.Workspace!.Members.Any(m =>
                     m.UserId == userId && m.IsActive) &&
                     (x.OwnerId == userId || x.AssigneeUserId == userId)));

            tasks = query.Scope switch
            {
                WorkTaskScope.Owned => tasks.Where(x => x.OwnerId == userId),
                WorkTaskScope.Assigned => tasks.Where(x => x.AssigneeUserId == userId),
                WorkTaskScope.Shared => tasks.Where(x => x.TaskShares.Any(s =>
                    s.SharedWithUserId == userId && s.Permission >= TaskPermission.View &&
                    s.Permission <= TaskPermission.Manage)),
                _ => tasks
            };

            var rows = tasks.Select(x => new WorkTaskQueryRow
            {
                TaskId = x.Id,
                SubTaskId = null,
                ParentTitle = null,
                ParentLocked = false,
                Version = x.Version,
                Title = x.Title,
                Description = x.Description,
                Category = x.Category,
                Status = x.Status,
                Priority = x.Priority,
                DueDate = x.DueDate,
                OwnerId = x.OwnerId,
                OwnerUserName = x.Owner.UserName,
                AssigneeUserId = x.AssigneeUserId,
                AssigneeUserName = x.Assignee != null ? x.Assignee.UserName : null,
                IsOwned = x.OwnerId == userId,
                IsAssigned = x.AssigneeUserId == userId,
                IsShared = x.TaskShares.Any(s => s.SharedWithUserId == userId &&
                    s.Permission >= TaskPermission.View && s.Permission <= TaskPermission.Manage),
                CurrentUserCanWork = x.WorkspaceId == null
                    ? x.TaskShares.Any(s => s.SharedWithUserId == userId &&
                        s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage)
                    : x.AssigneeUserId == userId && x.Workspace!.Members.Any(m =>
                        m.UserId == userId && m.IsActive),
                AssigneeCanWork = x.AssigneeUserId.HasValue &&
                    (x.WorkspaceId == null && x.TaskShares.Any(s =>
                         s.SharedWithUserId == x.AssigneeUserId.Value &&
                         s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage) ||
                     x.WorkspaceId != null && x.Workspace!.Members.Any(m =>
                         m.UserId == x.AssigneeUserId.Value && m.IsActive)),
                LatestSubmissionId = _context.TaskSubmissions.Where(s => s.TaskRequestId == x.Id)
                    .OrderByDescending(s => s.RevisionNumber).Select(s => (int?)s.Id).FirstOrDefault(),
                LatestSubmissionAuthorId = _context.TaskSubmissions.Where(s => s.TaskRequestId == x.Id)
                    .OrderByDescending(s => s.RevisionNumber).Select(s => (int?)s.SubmittedByUserId).FirstOrDefault(),
                LatestSubmissionCreatedAt = _context.TaskSubmissions.Where(s => s.TaskRequestId == x.Id)
                    .OrderByDescending(s => s.RevisionNumber).Select(s => (DateTime?)s.CreatedAt).FirstOrDefault(),
                LatestReviewDecision = _context.TaskSubmissions.Where(s => s.TaskRequestId == x.Id)
                    .OrderByDescending(s => s.RevisionNumber)
                    .Select(s => _context.TaskSubmissionReviews.Where(r => r.TaskSubmissionId == s.Id)
                        .Select(r => (TaskReviewDecision?)r.Decision).SingleOrDefault())
                    .FirstOrDefault(),
                CreatedAt = x.CreatedAt
            });

            if (query.Scope != WorkTaskScope.Shared)
            {
                var subtasks = DashboardSubTasks(userId);
                if (query.Scope == WorkTaskScope.Owned) subtasks = subtasks.Where(x => x.IsOwned);
                if (query.Scope == WorkTaskScope.Assigned) subtasks = subtasks.Where(x => x.IsAssigned);
                rows = rows.Concat(subtasks);
            }
            if (query.Search is not null)
            {
                var search = query.Search.ToLowerInvariant();
                rows = rows.Where(x => x.Title.ToLower().Contains(search) || x.Description.ToLower().Contains(search) ||
                    x.Category.ToLower().Contains(search) || (x.ParentTitle != null && x.ParentTitle.ToLower().Contains(search)));
            }
            if (query.Status.HasValue) rows = rows.Where(x => x.Status == query.Status.Value);
            if (query.Priority.HasValue) rows = rows.Where(x => x.Priority == query.Priority.Value);
            var soonThrough = todayUtc.AddDays(7);
            rows = query.Due switch
            {
                WorkTaskDueFilter.Overdue => rows.Where(x => x.DueDate < todayUtc &&
                    x.Status != TaskStatus.Completed && x.Status != TaskStatus.Cancelled),
                WorkTaskDueFilter.Today => rows.Where(x => x.DueDate == todayUtc),
                WorkTaskDueFilter.Soon => rows.Where(x => x.DueDate > todayUtc && x.DueDate <= soonThrough),
                WorkTaskDueFilter.None => rows.Where(x => !x.DueDate.HasValue),
                _ => rows
            };
            var totalCount = await rows.CountAsync();

            IOrderedQueryable<WorkTaskQueryRow> ordered = query.Sort switch
            {
                WorkTaskSort.Priority => rows.OrderBy(x => x.Priority == TaskPriority.Critical ? 0 :
                        x.Priority == TaskPriority.High ? 1 : x.Priority == TaskPriority.Medium ? 2 : 3)
                    .ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.TaskId),
                WorkTaskSort.Recent => rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TaskId),
                WorkTaskSort.ReviewAge => rows.OrderBy(x => !(!x.ParentLocked && x.IsOwned && x.Status == TaskStatus.InReview &&
                        x.AssigneeUserId.HasValue && x.AssigneeUserId != x.OwnerId && x.AssigneeCanWork &&
                        x.LatestSubmissionAuthorId == x.AssigneeUserId && x.LatestReviewDecision == null))
                    .ThenBy(x => x.LatestSubmissionCreatedAt).ThenBy(x => x.TaskId),
                _ => rows.OrderBy(x => !x.DueDate.HasValue).ThenBy(x => x.DueDate).ThenBy(x => x.TaskId)
            };

            var items = await ordered.ThenBy(x => x.SubTaskId.HasValue).ThenBy(x => x.SubTaskId)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
                .Select(x => new WorkTaskListItemDto
                {
                    TaskId = x.TaskId,
                    SubTaskId = x.SubTaskId,
                    ParentTitle = x.ParentTitle,
                    Version = x.Version,
                    Title = x.Title,
                    Description = x.Description,
                    Category = x.Category,
                    Status = x.Status,
                    Priority = x.Priority,
                    DueDate = x.DueDate,
                    OwnerId = x.OwnerId,
                    OwnerUserName = x.OwnerUserName,
                    AssigneeUserId = x.AssigneeUserId,
                    AssigneeUserName = x.AssigneeUserName,
                    IsOwned = x.IsOwned,
                    IsAssigned = x.IsAssigned,
                    IsShared = x.IsShared,
                    NextAction = x.ParentLocked ? WorkTaskNextAction.View
                        : x.IsOwned && x.Status == TaskStatus.InReview && x.AssigneeUserId.HasValue &&
                        x.AssigneeUserId != x.OwnerId && x.AssigneeCanWork &&
                        x.LatestSubmissionAuthorId == x.AssigneeUserId && x.LatestReviewDecision == null
                            ? WorkTaskNextAction.Review
                        : x.IsAssigned && !x.IsOwned && x.Status == TaskStatus.InProgress &&
                          x.CurrentUserCanWork && x.LatestSubmissionAuthorId == userId &&
                          x.LatestReviewDecision == TaskReviewDecision.ChangesRequested
                            ? WorkTaskNextAction.Revise
                        : x.IsAssigned && !x.IsOwned && x.Status == TaskStatus.InProgress &&
                          x.CurrentUserCanWork
                            ? WorkTaskNextAction.Submit
                        : x.Status == TaskStatus.Pending &&
                          (x.IsAssigned && (x.IsOwned || x.CurrentUserCanWork) ||
                           !x.AssigneeUserId.HasValue && x.IsOwned)
                            ? WorkTaskNextAction.Start
                            : WorkTaskNextAction.View,
                    LatestSubmissionId = x.LatestSubmissionId,
                    ReviewSubmittedAt = !x.ParentLocked && x.IsOwned && x.Status == TaskStatus.InReview &&
                        x.AssigneeUserId.HasValue && x.AssigneeUserId != x.OwnerId && x.AssigneeCanWork &&
                        x.LatestSubmissionAuthorId == x.AssigneeUserId && x.LatestReviewDecision == null
                            ? x.LatestSubmissionCreatedAt : null,
                    CreatedAt = x.CreatedAt
                }).ToListAsync();

            return new PagedWorkTasksDto
            {
                Items = items,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (totalCount + query.PageSize - 1) / query.PageSize
            };
        }

        private IQueryable<WorkTaskQueryRow> DashboardSubTasks(int userId) =>
            from x in _context.SubTasks.AsNoTracking()
            let parent = x.TaskRequest
            let owned = parent.OwnerId == userId
            let canWork = parent.WorkspaceId != null ? parent.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive)
                : owned || parent.TaskShares.Any(s => s.SharedWithUserId == userId &&
                    s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage)
            let assigned = x.AssigneeUserId == userId && canWork
            let assigneeCanWork = x.AssigneeUserId != null && (parent.WorkspaceId != null
                ? parent.Workspace!.Members.Any(m => m.UserId == x.AssigneeUserId && m.IsActive)
                : parent.TaskShares.Any(s => s.SharedWithUserId == x.AssigneeUserId &&
                    s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage))
            where x.Activity && parent.Activity && parent.Status != TaskStatus.Completed && parent.Status != TaskStatus.Cancelled &&
                (owned || assigned) && (parent.WorkspaceId == null || canWork)
            select new WorkTaskQueryRow
            {
                TaskId = parent.Id,
                SubTaskId = x.Id,
                ParentTitle = parent.Title,
                ParentLocked = parent.Status == TaskStatus.InReview,
                Version = x.Version,
                Title = x.Title,
                Description = x.Description,
                Category = parent.Category,
                Status = x.Status,
                Priority = x.Priority,
                DueDate = x.DueDate,
                OwnerId = parent.OwnerId,
                OwnerUserName = parent.Owner.UserName,
                AssigneeUserId = x.AssigneeUserId,
                AssigneeUserName = x.Assignee != null ? x.Assignee.UserName : null,
                IsOwned = owned,
                IsAssigned = assigned,
                IsShared = false,
                CurrentUserCanWork = canWork,
                AssigneeCanWork = assigneeCanWork,
                LatestSubmissionId = _context.SubTaskSubmissions.Where(s => s.SubTaskId == x.Id && s.TaskRequestId == parent.Id)
                    .OrderByDescending(s => s.RevisionNumber).Select(s => (int?)s.Id).FirstOrDefault(),
                LatestSubmissionAuthorId = _context.SubTaskSubmissions.Where(s => s.SubTaskId == x.Id && s.TaskRequestId == parent.Id)
                    .OrderByDescending(s => s.RevisionNumber).Select(s => (int?)s.SubmittedByUserId).FirstOrDefault(),
                LatestSubmissionCreatedAt = _context.SubTaskSubmissions.Where(s => s.SubTaskId == x.Id && s.TaskRequestId == parent.Id)
                    .OrderByDescending(s => s.RevisionNumber).Select(s => (DateTime?)s.CreatedAt).FirstOrDefault(),
                LatestReviewDecision = _context.SubTaskSubmissions.Where(s => s.SubTaskId == x.Id && s.TaskRequestId == parent.Id)
                    .OrderByDescending(s => s.RevisionNumber).Select(s => _context.SubTaskSubmissionReviews
                    .Where(r => r.SubTaskSubmissionId == s.Id).Select(r => (TaskReviewDecision?)r.Decision).SingleOrDefault())
                    .FirstOrDefault(),
                CreatedAt = x.CreatedAt
            };

        private sealed class WorkTaskQueryRow
        {
            public int TaskId { get; init; }
            public int? SubTaskId { get; init; }
            public string? ParentTitle { get; init; }
            public bool ParentLocked { get; init; }
            public long Version { get; init; }
            public string Title { get; init; } = null!;
            public string Description { get; init; } = null!;
            public string Category { get; init; } = null!;
            public TaskStatus Status { get; init; }
            public TaskPriority Priority { get; init; }
            public DateOnly? DueDate { get; init; }
            public int OwnerId { get; init; }
            public string OwnerUserName { get; init; } = null!;
            public int? AssigneeUserId { get; init; }
            public string? AssigneeUserName { get; init; }
            public bool IsOwned { get; init; }
            public bool IsAssigned { get; init; }
            public bool IsShared { get; init; }
            public bool CurrentUserCanWork { get; init; }
            public bool AssigneeCanWork { get; init; }
            public int? LatestSubmissionId { get; init; }
            public int? LatestSubmissionAuthorId { get; init; }
            public DateTime? LatestSubmissionCreatedAt { get; init; }
            public TaskReviewDecision? LatestReviewDecision { get; init; }
            public DateTime CreatedAt { get; init; }
        }
    }
}
