using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class TokenResponseDto:IDto
    {
        public string AccessToken { get; set; }
        public DateTime AccessTokenExpiration { get; set; }
        public string RefreshToken { get; set; }
    }
}
