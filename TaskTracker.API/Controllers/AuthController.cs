using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [Authorize]
        [HttpGet("profile")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Profile([FromServices] TaskTrackerDbContext db,
            [FromServices] ICurrentUserService currentUser, CancellationToken cancellationToken)
        {
            var username = await db.Users.Where(user => user.Id == currentUser.UserId && user.Status && user.IsVerified)
                .Select(user => user.UserName).SingleOrDefaultAsync(cancellationToken);
            return username is null ? NotFound() : Ok(new { username });
        }
        [HttpPost("register")]
        public async Task<IActionResult> Register(UserForRegisterDto dto)
        {
            var result = await _authService.RegisterAsync(dto);

            return result switch
            {
                { Success: true } => Ok(result),
                IConflictResult => Conflict(new
                {
                    code = "identity_conflict",
                    message = Messages.IdentityConflict
                }),
                _ when result.Message == Messages.VerificationEmailDeliveryFailed => Accepted(new
                {
                    code = "verification_delivery_failed",
                    message = Messages.VerificationEmailDeliveryFailed
                }),
                _ => BadRequest(new { code = "validation_failed", message = result.Message })
            };
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(UserForLoginDto dto)
        {
            var result = await _authService.LoginAsync(dto);

            return result switch
            {
                { Success: true } => Ok(result),
                _ when result.Message == Messages.EmailNotVerified => BadRequest(new
                {
                    code = "email_not_verified",
                    message = "Verify your email before signing in. You can request another code."
                }),
                _ when result.Message == Messages.PasswordError => BadRequest(new
                {
                    code = "invalid_credentials",
                    message = "Email or password is incorrect."
                }),
                _ => BadRequest(new { message = "Sign-in could not be completed. Please try again." })
            };
        }

        [HttpPost("verify-email")]
        [EnableRateLimiting(AuthRateLimits.Verify)]
        public async Task<IActionResult> VerifyEmail([FromBody] EmailVerificationDto dto)
        {
            var result = await _authService.VerifyEmailAsync(dto);
            if (!result.Success)
                return BadRequest(new
                {
                    message = "The verification code is invalid or expired. Check the latest email or request a new code."
                });

            return Ok(result);
        }

        [HttpPost("resend-verification")]
        [EnableRateLimiting(AuthRateLimits.Resend)]
        public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto dto)
        {
            var result = await _authService.ResendVerificationAsync(dto);
            return Ok(result);
        }
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto dto)
        {
            var result = await _authService.RefreshTokenAsync(dto);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
        {
            await _authService.LogoutAsync(dto);
            return NoContent();
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var result = await _authService.ForgotPasswordAsync(dto);
            return Ok(result);
        }

        [HttpPost("verify-password-reset-code")]
        public async Task<IActionResult> VerifyPasswordResetCode([FromBody] VerifyPasswordResetCodeDto dto)
        {
            var result = await _authService.VerifyPasswordResetCodeAsync(dto);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var result = await _authService.ResetPasswordAsync(dto);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var result = await _authService.ChangePasswordAsync(dto);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }





    }
}
