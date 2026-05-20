using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class AdminVerifyOtpDto:IDto
    {
        public string Username { get; set; } = null!;
        public string Code { get; set; } = null!;
    }
}
