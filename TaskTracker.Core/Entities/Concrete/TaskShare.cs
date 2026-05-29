using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete
{
    public class TaskShare : IEntity
    {
        public int Id { get; set; }

        public int TaskRequestId { get; set; }
        public int SharedWithUserId { get; set; }
        public TaskPermission Permission { get; set; } = TaskPermission.View;
        public TaskShareStatus Status { get; set; } = TaskShareStatus.Pending;
        public DateTime? SharedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public TaskRequest TaskRequest { get; set; } = null!;
        public User SharedWithUser { get; set; } = null!;
    }
}

