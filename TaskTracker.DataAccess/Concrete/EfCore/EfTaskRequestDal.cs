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
    }
}
