using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class AdminLoginDto : IDto
    {
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
    }
}
