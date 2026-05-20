using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;
using TaskTracker.Core.Utilities.Security.Jwt;

namespace TaskTracker.Entities.DTOs
{
    public class LoginResponseDto:IDto
    {
        public AccessToken AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }
}
