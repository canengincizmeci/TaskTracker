using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete
{
    public class Notification : IEntity
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public NotificationType Type { get; set; }
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        public bool IsRead { get; set; } = false;
        public int? RelatedEntityId { get; set; }
        public string? RedirectUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }
        public bool Activity { get; set; }
        public User User { get; set; } = null!;
    }
}

