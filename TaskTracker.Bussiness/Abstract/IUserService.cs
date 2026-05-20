using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Bussiness.Abstract
{
    public interface IUserService
    {
        Task AddAsync(User user);
        Task<User?> GetByMailAsync(string email);
        Task<List<OperationClaim>> GetClaimsAsync(User user);

    }
}
