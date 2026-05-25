using TaskTracker.Core.DataAccess.EfCore.Repository;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.DataAccess.Abstract
{
    public interface ITaskRequestDal:IEntityRepository<TaskRequest>
    {
        Task<bool> CanViewAsync(int taskId, int userId);
        Task<bool> CanEditAsync(int taskId, int userId);
        Task<bool> CanManageAsync(int taskId, int userId);




    }
}
