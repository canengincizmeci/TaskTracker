using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Core.DataAccess.EfCore.Repository;
using System.Linq.Expressions;
using TaskTracker.Core.Utilities.Enums;
using Microsoft.EntityFrameworkCore;

namespace TaskTracker.DataAccess.Concrete.EfCore
{
    public class EfTaskShareDal : EfEntityRepositoryBase<TaskShare, TaskTrackerDbContext>, ITaskShareDal
    {
        
        public EfTaskShareDal(TaskTrackerDbContext context) : base(context)
        {
        }

        public async Task<bool> HasPermissionAsync(int taskId, int userId, TaskPermission permission)
        {
            return await _context.TaskShares.AnyAsync(ts => ts.TaskRequestId == taskId && ts.SharedWithUserId == userId && ts.Permission >= permission && ts.Permission <= TaskPermission.Manage);
        }
        // Coordinate invitation writes with other changes to this task in the same SaveChanges transaction.
        public void TouchTask(TaskRequest task) => _context.Entry(task).Property(x => x.Version).IsModified = true;
        public Task ReloadInvitationAsync(TaskShareInvitation invitation) => _context.Entry(invitation).ReloadAsync();
        public async Task<TaskShare?> GetSharedTaskDetailsAsync(int taskShareId)
        {
            return await _context.TaskShares
                .Include(x => x.TaskRequest)
                .FirstOrDefaultAsync(x => x.Id == taskShareId);
        }
    }
}
