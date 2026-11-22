using System.Collections;
using TaskTracker.Core.DataAccess.EfCore.Repository;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;


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

        public Task<IDbContextTransaction> BeginTransactionAsync() => _context.Database.BeginTransactionAsync();

        public async Task<IDbContextTransaction?> BeginUserSessionWriteAsync(int userId)
        {
            if (!_context.Database.IsNpgsql())
                return null;

            var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                // All session writers lock the same user before reading or changing session state.
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""SELECT "Id" FROM "Users" WHERE "Id" = {userId} FOR UPDATE""");
                return transaction;
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }

        public void DiscardTrackedChanges() => _context.ChangeTracker.Clear();

        IEntityRepository<T> IUnitOfWork.GetRepository<T>()
        {
            _repositories ??= new Hashtable();

            var type = typeof(T).Name;

            if (!_repositories.ContainsKey(type))
            {
                var repositoryType = typeof(EfEntityRepositoryBase<,>);
                var repositoryInstance = Activator.CreateInstance(
                    repositoryType.MakeGenericType(typeof(T), typeof(TaskTrackerDbContext)),
                    _context
                );

                _repositories.Add(type, repositoryInstance);
            }

            return (IEntityRepository<T>)_repositories[type]!;
        }

        
    }
}
