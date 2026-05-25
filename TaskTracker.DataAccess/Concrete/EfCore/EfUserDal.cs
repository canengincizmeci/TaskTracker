using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.Repository;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.DataAccess.Abstract;

namespace TaskTracker.DataAccess.Concrete.EfCore
{
    public class EfUserDal : EfEntityRepositoryBase<User, TaskTrackerDbContext>, IUserDal     
    {
        private readonly TaskTrackerDbContext _driveContext;

        public EfUserDal(TaskTrackerDbContext context) : base(context)
        {
            _driveContext = context;
        }

       

        public List<OperationClaim> GetClaims(User user)
        {
            var result = from operationClaim in _driveContext.OperationClaims
                         join userOperationClaim in _driveContext.UserOperationClaims
                             on operationClaim.Id equals userOperationClaim.OperationClaimId
                         where userOperationClaim.UserId == user.Id
                         select new OperationClaim { Id = operationClaim.Id, Name = operationClaim.Name };
            return result.ToList();
        }

       
    }
}
