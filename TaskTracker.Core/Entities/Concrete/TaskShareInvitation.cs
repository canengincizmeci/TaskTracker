using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete
{
    public class TaskShareInvitation:IEntity
    {
        public int Id { get; set; }
        public int TaskRequestId { get; set; }
        public int InvitedUserId { get; set; }
        public int InvitedByUserId { get; set; }
        public TaskPermission Permission { get; set; }
        public TaskShareInvitationStatus Status { get; set; } = TaskShareInvitationStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public TaskRequest TaskRequest { get; set; } = null!;
        public User InvitedUser { get; set; } = null!;
        public User InvitedByUser { get; set; } = null!;
    }
}
