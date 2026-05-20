using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract
{
    public interface IAdminService
    {
        Task<IResult> LoginAsync(AdminLoginDto dto);
        Task<IDataResult<LoginResponseDto>> VerifyOtpAsync(AdminVerifyOtpDto dto);
    }
}
