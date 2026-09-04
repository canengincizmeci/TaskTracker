namespace TaskTracker.Core.Entities.Concrete
{
    public class PasswordResetRequest : IEntity
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string CodeHash { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public int FailedAttemptCount { get; set; } = 0;
        public DateTime? LockedUntil { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string? ResetTokenHash { get; set; }
        public DateTime? ResetTokenExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime? InvalidatedAt { get; set; }
        public User User { get; set; } = null!;
    }
}
