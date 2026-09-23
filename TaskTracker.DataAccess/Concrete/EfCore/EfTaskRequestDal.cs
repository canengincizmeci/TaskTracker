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
        {
            throw new NotImplementedException();
        }

        public Task<bool> CanManageAsync(int taskId, int userId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CanViewAsync(int taskId, int userId)
        {
            throw new NotImplementedException();
        }

        public async Task<List<TaskRequest>> GetTasksByUserIdAsync(int userId)
        {
            var tasks =await _context.TaskRequests.AsNoTracking().Include(t => t.Owner).Include(t => t.Assignee)
                .Include(t => t.TaskShares).Where(t=>t.Activity==true && (t.OwnerId==userId || t.TaskShares.Any(ts => ts.SharedWithUserId == userId && ts.Permission >= TaskPermission.View && ts.Permission <= TaskPermission.Manage))).OrderByDescending(t => t.CreatedAt).ToListAsync();
              
            return tasks;
        }  

        public Task<List<TaskRequest>> GetAssignedTasksAsync(int userId) => _context.TaskRequests.AsNoTracking()
            .Include(t => t.Owner).Include(t => t.Assignee).Include(t => t.TaskShares)
            .Where(t => t.Activity && t.AssigneeUserId == userId)
            .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        public async Task<int> GetLatestRevisionNumberAsync(int taskId) =>
            await _context.TaskSubmissions.Where(x => x.TaskRequestId == taskId)
                .MaxAsync(x => (int?)x.RevisionNumber) ?? 0;

        public Task<TaskSubmission?> GetLatestSubmissionAsync(int taskId) =>
            _context.TaskSubmissions.Include(x => x.SubmittedByUser)
                .Where(x => x.TaskRequestId == taskId)
                .OrderByDescending(x => x.RevisionNumber).FirstOrDefaultAsync();

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
                    x.Status != TaskStatus.Completed && x.Status != TaskStatus.Cancelled);

            var awaitingMyReviewCount = await _context.TaskRequests.AsNoTracking()
                .Where(x => x.Activity && x.OwnerId == userId && x.Status == TaskStatus.InReview &&
                    x.AssigneeUserId.HasValue && x.AssigneeUserId != x.OwnerId &&
                    x.TaskShares.Any(s => s.SharedWithUserId == x.AssigneeUserId.Value &&
                        s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage))
                .CountAsync(x => _context.TaskSubmissions
                    .Where(s => s.TaskRequestId == x.Id)
                    .OrderByDescending(s => s.RevisionNumber)
                    .Take(1)
                    .Any(s => s.SubmittedByUserId == x.AssigneeUserId &&
                        !_context.TaskSubmissionReviews.Any(r => r.TaskSubmissionId == s.Id)));

            var overdueCount = await _context.TaskRequests.AsNoTracking()
                .CountAsync(x => x.Activity && x.DueDate.HasValue && x.DueDate.Value < todayUtc &&
                    x.Status != TaskStatus.Completed && x.Status != TaskStatus.Cancelled &&
                    (x.OwnerId == userId || x.AssigneeUserId == userId));

            var pendingInvitationCount = await _context.TaskShareInvitations.AsNoTracking()
                .CountAsync(x => x.InvitedUserId == userId && x.Status == TaskShareInvitationStatus.Pending &&
                    (!x.ExpiresAt.HasValue || x.ExpiresAt > nowUtc) && x.TaskRequest.Activity &&
                    x.TaskRequest.Status != TaskStatus.Completed && x.TaskRequest.Status != TaskStatus.Cancelled);

            return new WorkDashboardSummaryDto
            {
                AssignedToMeCount = assignedToMeCount,
                AwaitingMyReviewCount = awaitingMyReviewCount,
                OverdueCount = overdueCount,
                PendingInvitationCount = pendingInvitationCount
            };
        }

        public async Task<PagedWorkTasksDto> GetWorkTasksAsync(
            int userId, WorkTaskQueryDto query, DateOnly todayUtc)
        {
            var tasks = _context.TaskRequests.AsNoTracking().Where(x => x.Activity &&
                (x.OwnerId == userId || x.AssigneeUserId == userId || x.TaskShares.Any(s =>
                    s.SharedWithUserId == userId && s.Permission >= TaskPermission.View &&
                    s.Permission <= TaskPermission.Manage)));

            tasks = query.Scope switch
            {
                WorkTaskScope.Owned => tasks.Where(x => x.OwnerId == userId),
                WorkTaskScope.Assigned => tasks.Where(x => x.AssigneeUserId == userId),
                WorkTaskScope.Shared => tasks.Where(x => x.TaskShares.Any(s =>
                    s.SharedWithUserId == userId && s.Permission >= TaskPermission.View &&
                    s.Permission <= TaskPermission.Manage)),
                _ => tasks
            };

            if (query.Search is not null)
            {
                var search = query.Search.ToLower();
                tasks = tasks.Where(x => x.Title.ToLower().Contains(search) ||
                    x.Description.ToLower().Contains(search) || x.Category.ToLower().Contains(search));
            }
            if (query.Status.HasValue) tasks = tasks.Where(x => x.Status == query.Status.Value);
            if (query.Priority.HasValue) tasks = tasks.Where(x => x.Priority == query.Priority.Value);

            var soonThrough = todayUtc.AddDays(7);
            tasks = query.Due switch
            {
                WorkTaskDueFilter.Overdue => tasks.Where(x => x.DueDate.HasValue && x.DueDate < todayUtc &&
                    x.Status != TaskStatus.Completed && x.Status != TaskStatus.Cancelled),
                WorkTaskDueFilter.Today => tasks.Where(x => x.DueDate == todayUtc),
                WorkTaskDueFilter.Soon => tasks.Where(x => x.DueDate > todayUtc && x.DueDate <= soonThrough),
                WorkTaskDueFilter.None => tasks.Where(x => !x.DueDate.HasValue),
                _ => tasks
            };

            var totalCount = await tasks.CountAsync();
            var rows = tasks.Select(x => new WorkTaskQueryRow
            {
                TaskId = x.Id,
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
                HasCurrentUserEditShare = x.TaskShares.Any(s => s.SharedWithUserId == userId &&
                    s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage),
                AssigneeHasEditShare = x.AssigneeUserId.HasValue && x.TaskShares.Any(s =>
                    s.SharedWithUserId == x.AssigneeUserId.Value && s.Permission >= TaskPermission.Edit &&
                    s.Permission <= TaskPermission.Manage),
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

            IOrderedQueryable<WorkTaskQueryRow> ordered = query.Sort switch
            {
                WorkTaskSort.Priority => rows.OrderBy(x => x.Priority == TaskPriority.Critical ? 0 :
                        x.Priority == TaskPriority.High ? 1 : x.Priority == TaskPriority.Medium ? 2 : 3)
                    .ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.TaskId),
                WorkTaskSort.Recent => rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TaskId),
                WorkTaskSort.ReviewAge => rows.OrderBy(x => !(x.IsOwned && x.Status == TaskStatus.InReview &&
                        x.AssigneeUserId.HasValue && x.AssigneeUserId != x.OwnerId && x.AssigneeHasEditShare &&
                        x.LatestSubmissionAuthorId == x.AssigneeUserId && x.LatestReviewDecision == null))
                    .ThenBy(x => x.LatestSubmissionCreatedAt).ThenBy(x => x.TaskId),
                _ => rows.OrderBy(x => !x.DueDate.HasValue).ThenBy(x => x.DueDate).ThenBy(x => x.TaskId)
            };

            var items = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
                .Select(x => new WorkTaskListItemDto
                {
                    TaskId = x.TaskId,
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
                    NextAction = x.IsOwned && x.Status == TaskStatus.InReview && x.AssigneeUserId.HasValue &&
                        x.AssigneeUserId != x.OwnerId && x.AssigneeHasEditShare &&
                        x.LatestSubmissionAuthorId == x.AssigneeUserId && x.LatestReviewDecision == null
                            ? WorkTaskNextAction.Review
                        : x.IsAssigned && !x.IsOwned && x.Status == TaskStatus.InProgress &&
                          x.HasCurrentUserEditShare && x.LatestSubmissionAuthorId == userId &&
                          x.LatestReviewDecision == TaskReviewDecision.ChangesRequested
                            ? WorkTaskNextAction.Revise
                        : x.IsAssigned && !x.IsOwned && x.Status == TaskStatus.InProgress &&
                          x.HasCurrentUserEditShare
                            ? WorkTaskNextAction.Submit
                        : x.Status == TaskStatus.Pending &&
                          (x.IsAssigned && (x.IsOwned || x.HasCurrentUserEditShare) ||
                           !x.AssigneeUserId.HasValue && x.IsOwned)
                            ? WorkTaskNextAction.Start
                            : WorkTaskNextAction.View,
                    LatestSubmissionId = x.LatestSubmissionId,
                    ReviewSubmittedAt = x.IsOwned && x.Status == TaskStatus.InReview &&
                        x.AssigneeUserId.HasValue && x.AssigneeUserId != x.OwnerId && x.AssigneeHasEditShare &&
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

        private sealed class WorkTaskQueryRow
        {
            public int TaskId { get; init; }
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
            public bool HasCurrentUserEditShare { get; init; }
            public bool AssigneeHasEditShare { get; init; }
            public int? LatestSubmissionId { get; init; }
            public int? LatestSubmissionAuthorId { get; init; }
            public DateTime? LatestSubmissionCreatedAt { get; init; }
            public TaskReviewDecision? LatestReviewDecision { get; init; }
            public DateTime CreatedAt { get; init; }
        }
    }
}
