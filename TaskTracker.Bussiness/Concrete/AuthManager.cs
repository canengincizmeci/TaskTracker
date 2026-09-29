using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Core.Aspects.Autofac;
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
        private readonly IPasswordHashService _passwordHashService;
        private readonly IIdentityNormalizer _identityNormalizer;

        public AuthManager(
            IUnitOfWork unitOfWork,
            ITokenHelper tokenHelper,
            IEmailService emailService,
            ICurrentUserService currentUserService,
            IConfiguration configuration,
            ILogger<AuthManager> logger,
            IPasswordHashService passwordHashService,
            IIdentityNormalizer identityNormalizer)
        {
            _unitOfWork = unitOfWork;
            _tokenHelper = tokenHelper;
            _emailService = emailService;
            _currentUserService = currentUserService;
            _configuration = configuration;
            _logger = logger;
            _passwordHashService = passwordHashService;
            _identityNormalizer = identityNormalizer;
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

            var normalizedEmail = _identityNormalizer.NormalizeEmail(dto.Email);
            var user = await userRepo.GetAsync(u => u.Email == normalizedEmail && u.IsVerified == true);
            if (user == null)
                return new ErrorDataResult<LoginResponseDto>(Messages.UserNotFound);

            if (!user.IsVerified)
                return new ErrorDataResult<LoginResponseDto>(Messages.EmailNotVerified);

            if (!user.Status)
                return new ErrorDataResult<LoginResponseDto>(Messages.UserPassive);

            var verificationOutcome = _passwordHashService.Verify(user, dto.Password);
            if (verificationOutcome == PasswordVerificationOutcome.Failed)
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

            if (verificationOutcome == PasswordVerificationOutcome.ValidNeedsUpgrade)
                ApplyNewPasswordHash(user, dto.Password);

            await refreshTokenRepo.AddAsync(refreshToken);
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
                when (verificationOutcome == PasswordVerificationOutcome.ValidNeedsUpgrade)
            {
                refreshTokenRepo.Detach(refreshToken);
                await userRepo.ReloadAsync(user);

                var currentOutcome = _passwordHashService.Verify(user, dto.Password);
                if (currentOutcome == PasswordVerificationOutcome.Failed)
                    return new ErrorDataResult<LoginResponseDto>(Messages.PasswordError);

                if (currentOutcome == PasswordVerificationOutcome.ValidNeedsUpgrade)
                    ApplyNewPasswordHash(user, dto.Password);

                accessToken = _tokenHelper.CreateToken(user, claims);
                refreshToken = _tokenHelper.CreateRefreshToken(user.Id);
                await refreshTokenRepo.AddAsync(refreshToken);

                try
                {
                    await _unitOfWork.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return new ErrorDataResult<LoginResponseDto>(Messages.PasswordCredentialChangedConcurrently);
                }
            }

            return new SuccessDataResult<LoginResponseDto>(
                new LoginResponseDto
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken.Token
                },
                Messages.SuccessfulLogin
            );
        }
        [ValidationAspect(typeof(UserForRegisterDtoValidator))]
        public async Task<IResult> RegisterAsync(UserForRegisterDto dto)
        {
            if (!IsNewPasswordLengthValid(dto.Password))
                return new ErrorResult(Messages.PasswordLengthInvalid);

            var userRepo = _unitOfWork.GetRepository<User>();
            var verificationRepo = _unitOfWork.GetRepository<EmailVerification>();
            var normalizedEmail = _identityNormalizer.NormalizeEmail(dto.Email);
            var displayUserName = _identityNormalizer.TrimUserName(dto.UserName);
            var normalizedUserName = _identityNormalizer.NormalizeUserName(dto.UserName);

            if (await userRepo.AnyAsync(u =>
                    u.Email == normalizedEmail || u.NormalizedUserName == normalizedUserName))
            {
                return new ConflictResult(Messages.IdentityConflict);
            }

            var user = new User
            {
                Email = normalizedEmail,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                UserName = displayUserName,
                NormalizedUserName = normalizedUserName,
                PasswordHash = [],
                PasswordSalt = [],
                Status = true,
                IsVerified = false,
                IsPhoneVerified = false
            };

            ApplyNewPasswordHash(user, dto.Password);

            await userRepo.AddAsync(user);
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsIdentityUniqueConflict(exception))
            {
                return new ConflictResult(Messages.IdentityConflict);
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

            var normalizedEmail = _identityNormalizer.NormalizeEmail(dto.Email);
            var user = await userRepo.GetAsync(u =>
                u.Email == normalizedEmail &&
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
            var normalizedEmail = _identityNormalizer.NormalizeEmail(dto.Email);
            var userRepo = _unitOfWork.GetRepository<User>();
            var passwordResetRepo = _unitOfWork.GetRepository<PasswordResetRequest>();

            var user = await userRepo.GetAsync(u =>
                u.Email == normalizedEmail &&
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
            var normalizedEmail = _identityNormalizer.NormalizeEmail(dto.Email);
            var userRepo = _unitOfWork.GetRepository<User>();
            var passwordResetRepo = _unitOfWork.GetRepository<PasswordResetRequest>();

            var user = await userRepo.GetAsync(u =>
                u.Email == normalizedEmail &&
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

        [ValidationAspect(typeof(ResetPasswordDtoValidator))]
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

            if (string.IsNullOrEmpty(dto.NewPassword) ||
                string.IsNullOrEmpty(dto.ConfirmNewPassword))
            {
                return new ErrorResult(Messages.PasswordResetPasswordRequired);
            }

            if (!IsNewPasswordLengthValid(dto.NewPassword) ||
                !IsNewPasswordLengthValid(dto.ConfirmNewPassword))
                return new ErrorResult(Messages.PasswordLengthInvalid);

            if (dto.NewPassword != dto.ConfirmNewPassword)
                return new ErrorResult(Messages.PasswordResetPasswordsDoNotMatch);

            ApplyNewPasswordHash(user, dto.NewPassword);
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
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return new ErrorResult(Messages.PasswordCredentialChangedConcurrently);
            }

            return new SuccessResult(Messages.PasswordResetSuccessful);
        }

        [ValidationAspect(typeof(ChangePasswordDtoValidator))]
        public async Task<IResult> ChangePasswordAsync(ChangePasswordDto dto)
        {
            if (string.IsNullOrEmpty(dto.CurrentPassword) ||
                string.IsNullOrEmpty(dto.NewPassword) ||
                string.IsNullOrEmpty(dto.ConfirmNewPassword))
            {
                return new ErrorResult(Messages.ChangePasswordFieldsRequired);
            }

            if (!IsNewPasswordLengthValid(dto.NewPassword) ||
                !IsNewPasswordLengthValid(dto.ConfirmNewPassword))
                return new ErrorResult(Messages.PasswordLengthInvalid);

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

            if (_passwordHashService.Verify(user, dto.CurrentPassword) == PasswordVerificationOutcome.Failed)
            {
                return new ErrorResult(Messages.CurrentPasswordIncorrect);
            }

            if (_passwordHashService.Verify(user, dto.NewPassword) != PasswordVerificationOutcome.Failed)
            {
                return new ErrorResult(Messages.NewPasswordMustBeDifferent);
            }

            ApplyNewPasswordHash(user, dto.NewPassword);

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
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return new ErrorResult(Messages.PasswordCredentialChangedConcurrently);
            }

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

        private void ApplyNewPasswordHash(User user, string password)
        {
            var passwordHash = _passwordHashService.CreateHash(user, password);
            user.PasswordHash = passwordHash.Hash;
            user.PasswordSalt = passwordHash.Salt;
            user.PasswordHashVersion = passwordHash.Version;
        }

        private static bool IsNewPasswordLengthValid(string? password) =>
            password is { Length: >= 12 and <= 128 };

        private static bool IsIdentityUniqueConflict(DbUpdateException exception)
        {
            if (exception.InnerException is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: "IX_Users_Email" or "IX_Users_NormalizedUserName"
                })
            {
                return true;
            }

            var message = exception.InnerException?.Message;
            return message?.Contains("UNIQUE constraint failed: Users.Email", StringComparison.OrdinalIgnoreCase) == true ||
                   message?.Contains("UNIQUE constraint failed: Users.NormalizedUserName", StringComparison.OrdinalIgnoreCase) == true;
        }




    }
}
