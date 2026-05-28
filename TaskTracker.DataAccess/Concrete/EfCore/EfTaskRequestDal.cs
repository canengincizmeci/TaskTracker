using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.Repository;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;

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
            var tasks =await _context.TaskRequests.Include(t => t.TaskShares).Where(t=>t.Activity==true && (t.OwnerId==userId || t.TaskShares.Any(ts => ts.SharedWithUserId == userId))).OrderByDescending(t => t.CreatedAt).ToListAsync();
              
            return tasks;
        }  
    }
} 
