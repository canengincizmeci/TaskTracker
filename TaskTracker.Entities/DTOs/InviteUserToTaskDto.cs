using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs
{
    public class InviteUserToTaskDto:IDto
    {
        public int TaskRequestId { get; set; }
        public string Username { get; set; }
        public TaskPermission Permission { get; set; } 
        public DateTime? SharedAt { get; set; }
    }
}
