namespace TaskTracker.API.Entitites
{
    public class AdminOtp
    {
        public int Id { get; set; }
        public int AdminId { get; set; }
        public Admin Admin { get; set; }
        public string Code { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
        public DateTime ExpireTime { get; set; } = DateTime.UtcNow;
        public bool IsUsed { get; set; }
        public int FailedAttemptCount { get; set; }
    }
}
