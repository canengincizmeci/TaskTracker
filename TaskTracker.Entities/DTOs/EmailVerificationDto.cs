using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class EmailVerificationDto:IDto
    {
        //public int Id { get; set; }
        //public int UserId { get; set; }
        public string Email { get; set; }
        public string Code { get; set; } = null!;
        //public bool IsVerified { get; set; } = false;
        //public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
