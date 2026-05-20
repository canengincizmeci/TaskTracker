using System.Collections;
using DrivingCourse.Core.DataAccess.EfCore.Repository;
using TaskTracker.Core.DataAccess.EfCore.Repository;

namespace TaskTracker.Core.DataAccess.EfCore.UnitOfWork
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly TaskTrackerDbContext _context;
        private Hashtable? _repositories;
        
        public UnitOfWork(TaskTrackerDbContext context)
        {
            _context = context;
        }
        public void Dispose()
        {
            _context.Dispose();
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        IEntityRepository<T> IUnitOfWork.GetRepository<T>()
        {
            _repositories ??= new Hashtable();

            var type = typeof(T).Name;

            if (!_repositories.ContainsKey(type))
            {
                var repositoryType = typeof(EfEntityRepositoryBase<>);
                var repositoryInstance = Activator.CreateInstance(
                    repositoryType.MakeGenericType(typeof(T)),
                    _context
                );

                _repositories.Add(type, repositoryInstance);
            }

            return (IEntityRepository<T>)_repositories[type]!;
        }

        
    }
}
