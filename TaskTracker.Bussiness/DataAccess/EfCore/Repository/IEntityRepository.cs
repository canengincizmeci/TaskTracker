using TaskTracker.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace TaskTracker.Core.DataAccess.EfCore.Repository
{
    public interface IEntityRepository<T> where T : class, IEntity
    {
        Task<List<T>> GetAllAsync(Expression<Func<T, bool>>? filter = null,
                             Func<IQueryable<T>, IQueryable<T>>? include = null);

        Task<T?> GetAsync(Expression<Func<T, bool>> filter,
                          Func<IQueryable<T>, IQueryable<T>>? include = null);
        Task AddAsync(T entity);

        Task<T> GetByIdAsync(int id);
     
        void Update(T entity);
        void Delete(T entity);
    }
}
