using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Core.Entities.Concrete
{
    public class EmailVerification:IEntity
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Code { get; set; }
        public bool IsVerified { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int FailedAttemptCount { get; set; } = 0;
        public DateTime? LockedUntil { get; set; }
        public long Version { get; set; }
        public Guid? DeliveryToken { get; set; }
        public bool DeliveryClaimed { get; set; }
        public virtual User User { get; set; } = null!;
    }
}
