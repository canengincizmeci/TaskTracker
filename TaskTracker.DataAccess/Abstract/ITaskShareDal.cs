using TaskTracker.Core.DataAccess.EfCore.Repository;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.DataAccess.Abstract
{
    public interface ITaskShareDal:IEntityRepository<TaskShare>
    {
        
        Task<bool> HasPermissionAsync(int taskId, int userId, TaskPermission permission);
        Task<TaskShare?> GetSharedTaskDetailsAsync(int taskShareId);
    }
}
