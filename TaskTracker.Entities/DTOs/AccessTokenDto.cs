using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class AccessTokenDto:IDto
    {
        public string Token { get; set; } = null!;
        public DateTime Expiration { get; set; }

        public string RefreshToken { get; set; } = null!;
    }
}
