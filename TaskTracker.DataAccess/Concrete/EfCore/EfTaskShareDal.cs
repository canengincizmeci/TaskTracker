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
            return await _context.TaskShares.AnyAsync(ts => ts.TaskRequestId == taskId && ts.SharedWithUserId == userId && ts.Permission >= permission);
        }
    }
}
