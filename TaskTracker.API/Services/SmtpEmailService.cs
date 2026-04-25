using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using TaskTracker.API.ConfModel;

namespace TaskTracker.API.Services
{
    public class SmtpEmailService : IEmailService
    {
        private readonly SmtpSettings _settings;

        public SmtpEmailService(IOptions<SmtpSettings> options)
        {
            _settings = options.Value;
        }

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            using var client = new SmtpClient(_settings.Host, _settings.Port);

            client.Credentials = new NetworkCredential(
                _settings.User,
                _settings.Pass
            );

            client.EnableSsl = true;

            using var mail = new MailMessage();

            mail.From = new MailAddress(_settings.FromEmail);
            mail.To.Add(to);
            mail.Subject = subject;
            mail.Body = body;
            mail.IsBodyHtml = false;

            await client.SendMailAsync(mail);
        }
    }
}
