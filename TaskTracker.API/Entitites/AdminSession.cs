namespace TaskTracker.API.Entitites
{
    public class AdminSession
    {
        public int Id { get; set; }

        public int AdminId { get; set; }

        public Admin Admin { get; set; }

        public string Token { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpireAt { get; set; }

        public bool IsRevoked { get; set; }
    }
}
