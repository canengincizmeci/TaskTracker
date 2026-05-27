using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Entities.DTOs
{
    public class UpdateTaskRequestDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public TaskPriority Priority { get; set; }
        public TaskStatus Status { get; set; }
        public bool Activity { get; set; }
        public DateOnly? DueDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

