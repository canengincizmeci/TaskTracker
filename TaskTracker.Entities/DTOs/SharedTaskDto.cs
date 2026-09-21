using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs
{
    public class SharedTaskDto
    {
        public int TaskId { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string Priority { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateOnly? DueDate { get; set; }
        public int OwnerId { get; set; }
        public string OwnerUserName { get; set; } = null!;
        public int? AssigneeUserId { get; set; }
        public string? AssigneeUserName { get; set; }
        public string? Permission { get; set; }
        public DateTime? SharedAt { get; set; }
    }
}
