using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Core.Utilities.Security.Hashing;
using TaskTracker.Core.Utilities.Security.Jwt;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete
{
    public class AuthManager: IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITokenHelper _tokenHelper;
        private readonly IEmailService _emailService;

        public AuthManager(IUnitOfWork unitOfWork, ITokenHelper tokenHelper, IEmailService emailService)
        {
            _unitOfWork = unitOfWork;
            _tokenHelper = tokenHelper;
            _emailService = emailService;
        }
        
        public async Task<IDataResult<AccessToken>> CreateAccessTokenAsync(User user)
        {
            var userOperationClaimRepo = _unitOfWork.GetRepository<UserOperationClaim>();

            var userOperationClaims = await userOperationClaimRepo
                .GetAllAsync(u => u.UserId == user.Id);

            var claims = userOperationClaims
                .Select(uoc => uoc.OperationClaim)
                .ToList();

            var accessToken = _tokenHelper.CreateToken(user, claims);
            return new SuccessDataResult<AccessToken>(accessToken, Messages.AccessTokenCreated);
        }

        public async Task<IDataResult<LoginResponseDto>> LoginAsync(UserForLoginDto dto)
        {
            var userRepo = _unitOfWork.GetRepository<User>();
            var refreshTokenRepo = _unitOfWork.GetRepository<RefreshToken>();

            var user = await userRepo.GetAsync(u => u.Email == dto.Email && u.IsVerified == true);
            if (user == null)
                return new ErrorDataResult<LoginResponseDto>(Messages.UserNotFound);

            if (!user.IsVerified)
                return new ErrorDataResult<LoginResponseDto>(Messages.EmailNotVerified);

            if (!user.Status)
                return new ErrorDataResult<LoginResponseDto>(Messages.UserPassive);

            if (!HashingHelper.VerifyPasswordHash(dto.Password, user.PasswordHash, user.PasswordSalt))
                return new ErrorDataResult<LoginResponseDto>(Messages.PasswordError);


            var claims = await GetUserClaimsAsync(user.Id);


            var accessToken = _tokenHelper.CreateToken(user, claims);
            var refreshToken = _tokenHelper.CreateRefreshToken(user.Id);


            await refreshTokenRepo.AddAsync(refreshToken);
            await _unitOfWork.SaveChangesAsync();

            return new SuccessDataResult<LoginResponseDto>(
                new LoginResponseDto
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken.Token
                },
                Messages.SuccessfulLogin
            );
        }


        public async Task<IDataResult<User>> RegisterAsync(UserForRegisterDto dto)
        {
            var userRepo = _unitOfWork.GetRepository<User>();
            var verificationRepo = _unitOfWork.GetRepository<EmailVerification>();


            byte[] passwordHash, passwordSalt;
            HashingHelper.CreatePasswordHash(dto.Password, out passwordHash, out passwordSalt);

            var user = new User
            {
                Email = dto.Email,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                Status = true,
                IsVerified = false,
                IsPhoneVerified = false
            };

            await userRepo.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();


            //var code = CodeGenerator.Generate6DigitCode();
            var code = "1234";

            var verification = new EmailVerification
            {
                UserId = user.Id,
                Code = code
            };

            await verificationRepo.AddAsync(verification);
            await _unitOfWork.SaveChangesAsync();


            //await _emailService.SendVerificationCodeAsync(user.Email, code);


            return new SuccessDataResult<User>(user, "Kayıt başarılı, mailine gönderilen kodu gir.");
        }

        public async Task<Core.Utilities.Results.IResult> UserExistsAsync(string email)
        {
            var userRepo = _unitOfWork.GetRepository<User>();

            var user = await userRepo.GetAsync(u => u.Email == email && u.IsVerified == true);
            if (user != null)
            {
                return new ErrorResult(Messages.UserAlreadyExists);
            }

            return new SuccessResult();
        }
        public async Task<Core.Utilities.Results.IResult> VerifyEmailAsync(EmailVerificationDto dto)
        {
            var verificationRepo = _unitOfWork.GetRepository<EmailVerification>();
            var userRepo = _unitOfWork.GetRepository<User>();

            var verification = await verificationRepo.GetAsync(v =>
                v.Code == dto.Code &&
                !v.IsVerified);

            if (verification is null)
                return new ErrorResult(Messages.CodeNotFound);

            if (verification.CreatedAt.AddMinutes(10) < DateTime.UtcNow)
                return new ErrorResult(Messages.CodeExpired);

            var user = await userRepo.GetByIdAsync(verification.UserId);
            if (user is null)
                return new ErrorResult(Messages.UserNotFound);

            verification.IsVerified = true;
            user.IsVerified = true;


            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.EmailIsCorrect);
        }
        public async Task<IDataResult<TokenResponseDto>> RefreshTokenAsync(RefreshTokenDto dto)
        {
            var refreshTokenRepo = _unitOfWork.GetRepository<RefreshToken>();
            var userRepo = _unitOfWork.GetRepository<User>();

            var refreshToken = await refreshTokenRepo.GetAsync(rt =>
                rt.Token == dto.RefreshToken &&
                !rt.IsRevoked &&
                rt.Expires > DateTime.UtcNow);

            if (refreshToken == null)
                return new ErrorDataResult<TokenResponseDto>(Messages.RefreshTokenInvalid);

            var user = await userRepo.GetByIdAsync(refreshToken.UserId);
            if (user == null)
                return new ErrorDataResult<TokenResponseDto>(Messages.UserNotFound);


            refreshToken.IsRevoked = true;
            refreshTokenRepo.Update(refreshToken);
            var claims = await GetUserClaimsAsync(user.Id);

            var accessToken = _tokenHelper.CreateToken(user, claims);
            var newRefreshToken = _tokenHelper.CreateRefreshToken(user.Id);

            await refreshTokenRepo.AddAsync(newRefreshToken);
            await _unitOfWork.SaveChangesAsync();

            //return new SuccessDataResult<AccessToken>(accessToken, Messages.AccessTokenCreated);
            return new SuccessDataResult<TokenResponseDto>(new TokenResponseDto
            {
                AccessToken = accessToken.Token,
                AccessTokenExpiration = accessToken.Expiration,
                RefreshToken = newRefreshToken.Token
            });
        }
        private async Task<List<OperationClaim>> GetUserClaimsAsync(int userId)
        {
            var userOperationClaimRepo =
                _unitOfWork.GetRepository<UserOperationClaim>();

            var operationClaimRepo =
                _unitOfWork.GetRepository<OperationClaim>();

            var userClaims = await userOperationClaimRepo
                .GetAllAsync(uoc => uoc.UserId == userId);

            var claimIds = userClaims.Select(u => u.OperationClaimId).ToList();

            var claims = await operationClaimRepo
                .GetAllAsync(oc => claimIds.Contains(oc.Id));

            return claims;
        }








    }
}
