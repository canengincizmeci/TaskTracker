using FluentValidation.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using TaskTracker.API.Context;
using TaskTracker.API.DTOs;
using TaskTracker.API.Entitites;
using TaskTracker.API.Services;
using Microsoft.EntityFrameworkCore;

namespace TaskTracker.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly MyDbContext _context;
        private readonly IEmailService _emailService;

        public AdminController(MyDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(AdminLoginDto adminDto)
        {
            var admin = await _context.Admins
                .FirstOrDefaultAsync(x => x.Username == adminDto.Username);

            if (admin == null)
                return Unauthorized("Username or password is wrong.");

            var passwordIsValid = BCrypt.Net.BCrypt.Verify(adminDto.Password, admin.PasswordHash);

            if (!passwordIsValid)
                return Unauthorized("Username or password is wrong.");

            var otpCode = GenerateOtpCode();

            var otp = new AdminOtp
            {
                AdminId = admin.Id,
                Code = BCrypt.Net.BCrypt.HashPassword(otpCode),
                CreatedTime = DateTime.UtcNow,
                ExpireTime = DateTime.UtcNow.AddMinutes(5),
                IsUsed = false,
                FailedAttemptCount = 0
            };

            _context.AdminOtps.Add(otp);
            await _context.SaveChangesAsync();

            await _emailService.SendEmailAsync(
                admin.Email,
                "TaskTracker Admin Login Code",
                $"Your login code is: {otpCode}. This code expires in 5 minutes."
            );

            return Ok("Verification code sent to admin email.");
        }

        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp(VerifyOtpDto dto)
        {
            var admin = await _context.Admins
                .FirstOrDefaultAsync(x => x.Username == dto.Username);

            if (admin == null)
                return Unauthorized("Invalid verification request.");

            var otp = await _context.AdminOtps
                .Where(x =>
                    x.AdminId == admin.Id &&
                    !x.IsUsed &&
                    x.ExpireTime > DateTime.UtcNow &&
                    x.FailedAttemptCount < 3)
                .OrderByDescending(x => x.CreatedTime)
                .FirstOrDefaultAsync();

            if (otp == null)
                return Unauthorized("Verification code expired or invalid.");

            var codeIsValid = BCrypt.Net.BCrypt.Verify(dto.Code, otp.Code);

            if (!codeIsValid)
            {
                otp.FailedAttemptCount++;
                await _context.SaveChangesAsync();

                return Unauthorized("Verification code is wrong.");
            }

            otp.IsUsed = true;
            await _context.SaveChangesAsync();

            var adminToken = GenerateSecureToken(); ;

            var session = new AdminSession
            {
                AdminId = admin.Id,
                Token = adminToken,
                CreatedAt = DateTime.UtcNow,
                ExpireAt = DateTime.UtcNow.AddHours(2),
                IsRevoked = false
            };

            _context.AdminSessions.Add(session);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                Message = "Admin login successful.",
                AdminToken = adminToken,
                ExpireAt = session.ExpireAt
            });
        }

        private static string GenerateOtpCode()
        {
            var number = RandomNumberGenerator.GetInt32(100000, 999999);
            return number.ToString();
        }
        private static string GenerateSecureToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes);
        }
        private bool IsAdminAuthorized()
        {
            if (!Request.Headers.TryGetValue("X-Admin-Token", out var token))
                return false;

            var tokenValue = token.ToString();

            return _context.AdminSessions.Any(x =>
                x.Token == tokenValue &&
                !x.IsRevoked &&
                x.ExpireAt > DateTime.UtcNow);
        }
    }
}
