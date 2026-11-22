using TaskTracker.Core.DataAccess.EfCore.Repository;
using TaskTracker.Core.Entities;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Text;


namespace TaskTracker.Core.DataAccess.EfCore.UnitOfWork
{
    public interface IUnitOfWork:IDisposable
    {
        IEntityRepository<T> GetRepository<T>() where T : class, IEntity;
        Task<int> SaveChangesAsync();
        Task<IDbContextTransaction> BeginTransactionAsync();
        Task<IDbContextTransaction?> BeginUserSessionWriteAsync(int userId);
        void DiscardTrackedChanges();
    }
}
