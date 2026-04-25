namespace TaskTracker.API.ConfModel
{
    public class SmtpSettings
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string User { get; set; }
        public string Pass { get; set; }
        public string FromEmail { get; set; }
    }
}
