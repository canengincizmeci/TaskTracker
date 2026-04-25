namespace TaskTracker.API.Entitites
{
    public class Admin
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public List<AdminOtp> AdminOtps { get; set; } = new();
        public List<AdminSession> AdminSessions { get; set; } = new();
    }
}
