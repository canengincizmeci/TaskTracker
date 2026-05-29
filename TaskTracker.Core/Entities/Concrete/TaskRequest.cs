using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Core.Entities.Concrete
{
    public class TaskRequest : IEntity
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public required string Title { get; set; }
        public required string Description { get; set; } 
        public required string Category { get; set; }
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        public bool Activity { get; set; }
        public int SharedCount { get; set; }
        public DateOnly? DueDate { get; set; } 
        public TaskVisibility Visibility { get; set; } = TaskVisibility.Private;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User Owner { get; set; } = null!;
        public ICollection<TaskShare> TaskShares { get; set; } = new List<TaskShare>();
    }
}


