using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.Repository;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.DataAccess.Abstract;

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
    }
}
