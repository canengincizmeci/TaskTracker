using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Core.Utilities.Security.Cryptography;
using TaskTracker.Core.Utilities.Security.Hashing;
using TaskTracker.Core.Utilities.Security.Jwt;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete
{
    public class AuthManager : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITokenHelper _tokenHelper;
        private readonly IEmailService _emailService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthManager> _logger;

        public AuthManager(
            IUnitOfWork unitOfWork,
            ITokenHelper tokenHelper,
            IEmailService emailService,
            ICurrentUserService currentUserService,
            IConfiguration configuration,
            ILogger<AuthManager> logger)
        {
            _unitOfWork = unitOfWork;
            _tokenHelper = tokenHelper;
            _emailService = emailService;
            _currentUserService = currentUserService;
            _configuration = configuration;
            _logger = logger;
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
            Console.WriteLine("=== BEFORE TOKEN CREATE ===");
            Console.WriteLine($"DTO Email: {dto.Email}");
            Console.WriteLine($"User Id: {user.Id}");
            Console.WriteLine($"User Email: {user.Email}");
            Console.WriteLine($"User Name: {user.FirstName} {user.LastName}");

            foreach (var claim in claims)
            {
                Console.WriteLine($"Role Claim: {claim.Name}");
            }
            Console.WriteLine("===========================");

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


        //public async Task<IDataResult<User>> RegisterAsync(UserForRegisterDto dto)
        //{
        //    var userRepo = _unitOfWork.GetRepository<User>();
        //    var verificationRepo = _unitOfWork.GetRepository<EmailVerification>();


        //    byte[] passwordHash, passwordSalt;
        //    HashingHelper.CreatePasswordHash(dto.Password, out passwordHash, out passwordSalt);

        //    var user = new User
        //    {
        //        Email = dto.Email,
        //        FirstName = dto.FirstName,
        //        LastName = dto.LastName,
        //        UserName = dto.UserName,
        //        PasswordHash = passwordHash,
        //        PasswordSalt = passwordSalt,
        //        Status = true,
        //        IsVerified = false,
        //        IsPhoneVerified = false
        //    };

        //    await userRepo.AddAsync(user);
        //    await _unitOfWork.SaveChangesAsync();


        //    var code = CodeGenerator.Generate6DigitCode();


        //    var verification = new EmailVerification
        //    {
        //        UserId = user.Id,
        //        Code = code
        //    };

        //    await verificationRepo.AddAsync(verification);
        //    await _unitOfWork.SaveChangesAsync();


        //    await _emailService.SendVerificationCodeAsync(user.Email, code);


        //    return new SuccessDataResult<User>(user, "Kayıt başarılı, mailine gönderilen kodu gir.");
        ////}
        public async Task<IResult> RegisterAsync(UserForRegisterDto dto)
        {
            var userRepo = _unitOfWork.GetRepository<User>();
            var verificationRepo = _unitOfWork.GetRepository<EmailVerification>();

            byte[] passwordHash, passwordSalt;
            HashingHelper.CreatePasswordHash(dto.Password, out passwordHash, out passwordSalt);

            var existingUser = await userRepo.GetAsync(u => u.Email == dto.Email);

            User user;

            if (existingUser != null && existingUser.IsVerified)
            {
                return new ErrorResult(Messages.UserAlreadyExists);
            }

            if (existingUser != null && !existingUser.IsVerified)
            {
                existingUser.FirstName = dto.FirstName;
                existingUser.LastName = dto.LastName;
                existingUser.UserName = dto.UserName;
                existingUser.PasswordHash = passwordHash;
                existingUser.PasswordSalt = passwordSalt;
                existingUser.Status = true;
                existingUser.IsPhoneVerified = false;

                userRepo.Update(existingUser);
                user = existingUser;
            }
            else
            {
                user = new User
                {
                    Email = dto.Email,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    UserName = dto.UserName,
                    PasswordHash = passwordHash,
                    PasswordSalt = passwordSalt,
                    Status = true,
                    IsVerified = false,
                    IsPhoneVerified = false
                };

                await userRepo.AddAsync(user);
                await _unitOfWork.SaveChangesAsync();
            }

            var oldVerifications = await verificationRepo.GetAllAsync(v =>
                v.UserId == user.Id &&
                !v.IsVerified);

            foreach (var oldVerification in oldVerifications)
            {
                oldVerification.IsVerified = true;
                verificationRepo.Update(oldVerification);
            }

            var code = CodeGenerator.Generate6DigitCode();

            var verification = new EmailVerification
            {
                UserId = user.Id,
                Code = code,
                IsVerified = false,
                CreatedAt = DateTime.UtcNow
            };

            await verificationRepo.AddAsync(verification);
            await _unitOfWork.SaveChangesAsync();

            await _emailService.SendVerificationCodeAsync(user.Email, code);

            return new SuccessResult("Kayıt başarılı, mailine gönderilen kodu gir.");
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
        //public async Task<Core.Utilities.Results.IResult> VerifyEmailAsync(EmailVerificationDto dto)
        //{
        //    var verificationRepo = _unitOfWork.GetRepository<EmailVerification>();
        //    var userRepo = _unitOfWork.GetRepository<User>();

        //    var verification = await verificationRepo.GetAsync(v =>
        //        v.Code == dto.Code &&
        //        !v.IsVerified);

        //    if (verification is null)
        //        return new ErrorResult(Messages.CodeNotFound);

        //    if (verification.CreatedAt.AddMinutes(10) < DateTime.UtcNow)
        //        return new ErrorResult(Messages.CodeExpired);

        //    var user = await userRepo.GetByIdAsync(verification.UserId);
        //    if (user is null)
        //        return new ErrorResult(Messages.UserNotFound);

        //    verification.IsVerified = true;
        //    user.IsVerified = true;
        //    await AddUserClaimToUserAsync(user.Id);

        //    await _unitOfWork.SaveChangesAsync();

        //    return new SuccessResult(Messages.EmailIsCorrect);
        //}
        public async Task<Core.Utilities.Results.IResult> VerifyEmailAsync(EmailVerificationDto dto)
        {
            var verificationRepo = _unitOfWork.GetRepository<EmailVerification>();
            var userRepo = _unitOfWork.GetRepository<User>();

            var user = await userRepo.GetAsync(u =>
                u.Email == dto.Email &&
                u.IsVerified == false);

            if (user is null)
                return new ErrorResult(Messages.UserNotFound);

            var verification = await verificationRepo.GetAsync(v =>
                v.UserId == user.Id &&
                v.Code == dto.Code &&
                !v.IsVerified);

            if (verification is null)
                return new ErrorResult(Messages.CodeNotFound);

            if (verification.CreatedAt.AddMinutes(10) < DateTime.UtcNow)
                return new ErrorResult(Messages.CodeExpired);

            verification.IsVerified = true;
            user.IsVerified = true;

            await AddUserClaimToUserAsync(user.Id);

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

        public async Task<IResult> ForgotPasswordAsync(ForgotPasswordDto dto)
        {
            const int otpLifetimeMinutes = 10;
            const int resendCooldownSeconds = 60;

            var genericResult = new SuccessResult(Messages.PasswordRecoveryInstructionsSent);
            var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
            var userRepo = _unitOfWork.GetRepository<User>();
            var passwordResetRepo = _unitOfWork.GetRepository<PasswordResetRequest>();

            var user = await userRepo.GetAsync(u =>
                u.Email.ToLower() == normalizedEmail &&
                u.Status &&
                u.IsVerified);

            if (user is null)
                return genericResult;

            var now = DateTime.UtcNow;
            var activeRequests = await passwordResetRepo.GetAllAsync(r =>
                r.UserId == user.Id &&
                r.UsedAt == null &&
                r.InvalidatedAt == null &&
                (r.ExpiresAt > now ||
                 (r.ResetTokenExpiresAt.HasValue && r.ResetTokenExpiresAt.Value > now)));

            if (activeRequests.Any(r => r.CreatedAt > now.AddSeconds(-resendCooldownSeconds)))
                return genericResult;

            var hmacSecret = _configuration["PasswordRecovery:HmacSecret"]!;

            foreach (var activeRequest in activeRequests)
            {
                activeRequest.InvalidatedAt = now;
                passwordResetRepo.Update(activeRequest);
            }

            var code = CodeGenerator.Generate6DigitCode();
            var request = new PasswordResetRequest
            {
                UserId = user.Id,
                CodeHash = PasswordResetCodeHasher.Hash(normalizedEmail, code, hmacSecret),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(otpLifetimeMinutes),
                FailedAttemptCount = 0
            };

            await passwordResetRepo.AddAsync(request);
            await _unitOfWork.SaveChangesAsync();

            try
            {
                await _emailService.SendPasswordResetCodeAsync(user.Email, code);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Password reset email delivery failed.");

                try
                {
                    request.InvalidatedAt = DateTime.UtcNow;
                    passwordResetRepo.Update(request);
                    await _unitOfWork.SaveChangesAsync();
                }
                catch (Exception invalidationException)
                {
                    _logger.LogError(invalidationException, "Failed to invalidate an undelivered password reset request.");
                }
            }

            return genericResult;
        }

        public async Task<IDataResult<PasswordResetTokenDto>> VerifyPasswordResetCodeAsync(
            VerifyPasswordResetCodeDto dto)
        {
            const int maximumAttempts = 5;
            const int resetTokenLifetimeMinutes = 15;

            var failureResult = new ErrorDataResult<PasswordResetTokenDto>(
                Messages.PasswordResetCodeInvalidOrExpired);
            var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
            var userRepo = _unitOfWork.GetRepository<User>();
            var passwordResetRepo = _unitOfWork.GetRepository<PasswordResetRequest>();

            var user = await userRepo.GetAsync(u =>
                u.Email.ToLower() == normalizedEmail &&
                u.Status &&
                u.IsVerified);

            if (user is null)
                return failureResult;

            var requests = await passwordResetRepo.GetAllAsync(r => r.UserId == user.Id);
            var currentRequest = requests
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .FirstOrDefault();

            if (currentRequest is null)
                return failureResult;

            var now = DateTime.UtcNow;
            var isUnusable =
                currentRequest.UsedAt.HasValue ||
                currentRequest.InvalidatedAt.HasValue ||
                currentRequest.VerifiedAt.HasValue ||
                currentRequest.ExpiresAt <= now ||
                currentRequest.FailedAttemptCount >= maximumAttempts ||
                (currentRequest.LockedUntil.HasValue && currentRequest.LockedUntil.Value > now);

            if (isUnusable)
                return failureResult;

            var isValidCodeFormat =
                dto.Code is { Length: 6 } &&
                dto.Code.All(character => character >= '0' && character <= '9');
            var hmacSecret = _configuration["PasswordRecovery:HmacSecret"]!;
            var isValidCode = isValidCodeFormat && PasswordResetCodeHasher.Verify(
                normalizedEmail,
                dto.Code,
                hmacSecret,
                currentRequest.CodeHash);

            if (!isValidCode)
            {
                currentRequest.FailedAttemptCount++;

                if (currentRequest.FailedAttemptCount >= maximumAttempts)
                {
                    currentRequest.FailedAttemptCount = maximumAttempts;
                    currentRequest.LockedUntil = currentRequest.ExpiresAt;
                }

                passwordResetRepo.Update(currentRequest);
                await _unitOfWork.SaveChangesAsync();
                return failureResult;
            }

            var resetToken = PasswordResetTokenGenerator.GenerateToken();
            currentRequest.VerifiedAt = now;
            currentRequest.ResetTokenHash = PasswordResetTokenGenerator.HashToken(resetToken);
            currentRequest.ResetTokenExpiresAt = now.AddMinutes(resetTokenLifetimeMinutes);

            passwordResetRepo.Update(currentRequest);
            await _unitOfWork.SaveChangesAsync();

            return new SuccessDataResult<PasswordResetTokenDto>(
                new PasswordResetTokenDto
                {
                    ResetToken = resetToken,
                    ExpiresAt = currentRequest.ResetTokenExpiresAt.Value
                },
                Messages.PasswordResetCodeVerified);
        }

        public async Task<IResult> ResetPasswordAsync(ResetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ResetToken))
                return new ErrorResult(Messages.PasswordResetTokenInvalidOrExpired);

            var now = DateTime.UtcNow;
            var resetTokenHash = PasswordResetTokenGenerator.HashToken(dto.ResetToken);
            var passwordResetRepo = _unitOfWork.GetRepository<PasswordResetRequest>();
            var userRepo = _unitOfWork.GetRepository<User>();
            var refreshTokenRepo = _unitOfWork.GetRepository<RefreshToken>();

            var currentRequest = await passwordResetRepo.GetAsync(r =>
                r.ResetTokenHash == resetTokenHash);

            var isUsableRequest =
                currentRequest is not null &&
                currentRequest.VerifiedAt.HasValue &&
                !currentRequest.UsedAt.HasValue &&
                !currentRequest.InvalidatedAt.HasValue &&
                currentRequest.ResetTokenHash is not null &&
                currentRequest.ResetTokenExpiresAt.HasValue &&
                currentRequest.ResetTokenExpiresAt.Value > now;

            if (!isUsableRequest)
                return new ErrorResult(Messages.PasswordResetTokenInvalidOrExpired);

            var validRequest = currentRequest!;
            var user = await userRepo.GetAsync(u =>
                u.Id == validRequest.UserId &&
                u.Status &&
                u.IsVerified);

            if (user is null)
                return new ErrorResult(Messages.PasswordResetTokenInvalidOrExpired);

            if (string.IsNullOrWhiteSpace(dto.NewPassword) ||
                string.IsNullOrWhiteSpace(dto.ConfirmNewPassword))
            {
                return new ErrorResult(Messages.PasswordResetPasswordRequired);
            }

            if (dto.NewPassword.Length > 128 || dto.ConfirmNewPassword.Length > 128)
                return new ErrorResult(Messages.PasswordResetPasswordTooLong);

            if (dto.NewPassword != dto.ConfirmNewPassword)
                return new ErrorResult(Messages.PasswordResetPasswordsDoNotMatch);

            HashingHelper.CreatePasswordHash(
                dto.NewPassword,
                out var passwordHash,
                out var passwordSalt);

            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            validRequest.UsedAt = now;

            var otherResetRequests = await passwordResetRepo.GetAllAsync(r =>
                r.UserId == user.Id &&
                r.Id != validRequest.Id &&
                r.UsedAt == null &&
                r.InvalidatedAt == null);

            foreach (var otherResetRequest in otherResetRequests)
            {
                otherResetRequest.InvalidatedAt = now;
                passwordResetRepo.Update(otherResetRequest);
            }

            var activeRefreshTokens = await refreshTokenRepo.GetAllAsync(r =>
                r.UserId == user.Id &&
                !r.IsRevoked);

            foreach (var refreshToken in activeRefreshTokens)
            {
                refreshToken.IsRevoked = true;
                refreshTokenRepo.Update(refreshToken);
            }

            userRepo.Update(user);
            passwordResetRepo.Update(validRequest);
            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.PasswordResetSuccessful);
        }

        public async Task<IResult> ChangePasswordAsync(ChangePasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
                string.IsNullOrWhiteSpace(dto.NewPassword) ||
                string.IsNullOrWhiteSpace(dto.ConfirmNewPassword))
            {
                return new ErrorResult(Messages.ChangePasswordFieldsRequired);
            }

            if (dto.NewPassword.Length > 128 || dto.ConfirmNewPassword.Length > 128)
                return new ErrorResult(Messages.ChangePasswordTooLong);

            if (dto.NewPassword != dto.ConfirmNewPassword)
                return new ErrorResult(Messages.ChangePasswordPasswordsDoNotMatch);

            var currentUserId = _currentUserService.UserId;
            var userRepo = _unitOfWork.GetRepository<User>();
            var refreshTokenRepo = _unitOfWork.GetRepository<RefreshToken>();
            var passwordResetRepo = _unitOfWork.GetRepository<PasswordResetRequest>();

            var user = await userRepo.GetAsync(u =>
                u.Id == currentUserId &&
                u.Status &&
                u.IsVerified);

            if (user is null)
                return new ErrorResult(Messages.ChangePasswordUnavailable);

            if (!HashingHelper.VerifyPasswordHash(
                    dto.CurrentPassword,
                    user.PasswordHash,
                    user.PasswordSalt))
            {
                return new ErrorResult(Messages.CurrentPasswordIncorrect);
            }

            if (HashingHelper.VerifyPasswordHash(
                    dto.NewPassword,
                    user.PasswordHash,
                    user.PasswordSalt))
            {
                return new ErrorResult(Messages.NewPasswordMustBeDifferent);
            }

            HashingHelper.CreatePasswordHash(
                dto.NewPassword,
                out var passwordHash,
                out var passwordSalt);

            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;

            var activeRefreshTokens = await refreshTokenRepo.GetAllAsync(rt =>
                rt.UserId == user.Id &&
                !rt.IsRevoked);

            foreach (var refreshToken in activeRefreshTokens)
            {
                refreshToken.IsRevoked = true;
                refreshTokenRepo.Update(refreshToken);
            }

            var now = DateTime.UtcNow;
            var activePasswordResetRequests = await passwordResetRepo.GetAllAsync(request =>
                request.UserId == user.Id &&
                request.UsedAt == null &&
                request.InvalidatedAt == null);

            foreach (var passwordResetRequest in activePasswordResetRequests)
            {
                passwordResetRequest.InvalidatedAt = now;
                passwordResetRepo.Update(passwordResetRequest);
            }

            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.PasswordChangeSuccessful);
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


        private async Task AddUserClaimToUserAsync(int id)
        {
          
            var userOperationClaimRepo = _unitOfWork.GetRepository<UserOperationClaim>();
            await userOperationClaimRepo.AddAsync(new UserOperationClaim
            {
                UserId = id,
                OperationClaimId = 2
            });
         
        }




    }
}
