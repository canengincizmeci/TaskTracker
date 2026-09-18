using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs
{
    public class TaskInvitationDto:IDto
    {
        public int Id { get; set; }
        public int TaskRequestId { get; set; }
        public string TaskTitle { get; set; } = null!;
        public string InviterUserName { get; set; } = null!;
        public string? Permission { get; set; }
        public string Status { get; set; } = null!;
        public bool CanAccept { get; set; }
        public bool CanReject { get; set; }
        public string? UnavailableReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
