using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class RefreshTokenDto:IDto
    {
        public string RefreshToken { get; set; } = null!;
    }
}
