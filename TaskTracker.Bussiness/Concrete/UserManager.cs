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
        private readonly IIdentityNormalizer _identityNormalizer;

        public UserManager(IUnitOfWork unitOfWork, IIdentityNormalizer identityNormalizer)
        {
            _unitOfWork = unitOfWork;
            _identityNormalizer = identityNormalizer;
        }
        public async Task AddAsync(User user)
        {
            var userRepo = _unitOfWork.GetRepository<User>();
            user.Email = _identityNormalizer.NormalizeEmail(user.Email);
            user.UserName = _identityNormalizer.TrimUserName(user.UserName);
            user.NormalizedUserName = _identityNormalizer.NormalizeUserName(user.UserName);
            await userRepo.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<User?> GetByMailAsync(string email)
        {
            var userRepo = _unitOfWork.GetRepository<User>();
            var normalizedEmail = _identityNormalizer.NormalizeEmail(email);
            var user = await userRepo.GetAsync(
                u => u.Email == normalizedEmail,
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
