using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;


namespace TaskTracker.Core.DataAccess.EfCore.Repository
{
   
    public class EfEntityRepositoryBase<T> : IEntityRepository<T>
        where T : class, IEntity
    {
        protected readonly DbContext _context;
        protected readonly DbSet<T> _dbSet;

        public EfEntityRepositoryBase(DbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public async Task AddAsync(T entity)
        {
            await _dbSet.AddAsync(entity);
        }

        public void Delete(T entity)
        {
            _dbSet.Remove(entity);
        }

        public async Task<List<T>> GetAllAsync(Expression<Func<T, bool>>? filter = null,
                                        Func<IQueryable<T>, IQueryable<T>>? include = null)
        {
            IQueryable<T> query = _context.Set<T>();

            if (include != null)
                query = include(query);

            if (filter != null)
                query = query.Where(filter);

            return await query.ToListAsync();
        }

        public async Task<T?> GetAsync(Expression<Func<T, bool>> filter,Func<IQueryable<T>, IQueryable<T>>? include = null)
        {
            IQueryable<T> query = _context.Set<T>();

            if (include != null)
                query = include(query);

            return await query.FirstOrDefaultAsync(filter);
        }

        public async Task<T> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        public void Update(T entity)
        {
            _dbSet.Update(entity);
        }

        
    }
}
