using TaskTracker.Core.DataAccess.EfCore.Repository;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.DataAccess.Abstract
{
    public interface ITaskRequestDal:IEntityRepository<TaskRequest>
    {
        Task<bool> CanViewAsync(int taskId, int userId);
        Task<bool> CanEditAsync(int taskId, int userId);
        Task<bool> CanManageAsync(int taskId, int userId);
        Task<List<TaskRequest>> GetTasksByUserIdAsync(int userId);
        


    }
}
