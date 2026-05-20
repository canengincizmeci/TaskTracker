using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Core.Utilities.Security.Jwt;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract
{
    public interface IAuthService
    {
        Task<IDataResult<User>> RegisterAsync(UserForRegisterDto dto);
        Task<IDataResult<LoginResponseDto>> LoginAsync(UserForLoginDto dto);

        Task<IResult> UserExistsAsync(string email);
        Task<IDataResult<AccessToken>> CreateAccessTokenAsync(User user);
        Task<IDataResult<TokenResponseDto>> RefreshTokenAsync(RefreshTokenDto dto);

        Task<IResult> VerifyEmailAsync(EmailVerificationDto dto);
    }
}
