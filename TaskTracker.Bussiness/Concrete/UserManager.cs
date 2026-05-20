using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Bussiness.Concrete
{
    public class UserManager: IUserService
    {
        private readonly IUnitOfWork _unitOfWork;

        public UserManager(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        public async Task AddAsync(User user)
        {
            var userRepo = _unitOfWork.GetRepository<User>();
            await userRepo.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<User?> GetByMailAsync(string email)
        {
            var userRepo = _unitOfWork.GetRepository<User>();
            var user = await userRepo.GetAsync(
                u => u.Email == email,
                include: q => q.Include(u => u.UserOperationClaims)
                               .ThenInclude(uoc => uoc.OperationClaim)
            );
            return user;
        }

        public async Task<List<OperationClaim>> GetClaimsAsync(User user)
        {
            var claims = user.UserOperationClaims
                .Select(uoc => uoc.OperationClaim)
                .ToList();

            return claims;
        }


    }
}
