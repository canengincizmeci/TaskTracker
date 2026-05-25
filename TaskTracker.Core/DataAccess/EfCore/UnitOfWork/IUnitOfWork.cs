using TaskTracker.Core.DataAccess.EfCore.Repository;
using TaskTracker.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;


namespace TaskTracker.Core.DataAccess.EfCore.UnitOfWork
{
    public interface IUnitOfWork:IDisposable
    {
        IEntityRepository<T> GetRepository<T>() where T : class, IEntity;
        Task<int> SaveChangesAsync();
    }
}
