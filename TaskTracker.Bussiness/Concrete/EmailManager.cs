using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using TaskTracker.Bussiness.Abstract;

namespace TaskTracker.Bussiness.Concrete
{
    public class EmailManager : IEmailService
    {
        private readonly string _smtpHost;
        private readonly int? _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPass;
        private readonly string _fromEmail;
        public EmailManager(IConfiguration configuration)
        {

            _smtpHost = configuration["Smtp:Host"]!;
            _smtpPort = int.TryParse(configuration["Smtp:Port"], out var port) && port is > 0 and <= 65535 ? port : null;
            _smtpUser = configuration["Smtp:User"]!;
            _smtpPass = configuration["Smtp:Pass"]!;
            _fromEmail = configuration["Smtp:FromEmail"]!;
        }

        public async Task SendTaskShareInvitationEmailAsync(string email, string taskTitle, string inviterUsername, string invitationUrl)
        {
            using var client = CreateClient();

            using var message = new MailMessage(_fromEmail, email)
            {
                Subject = "TaskTracker - New Task Invitation",
                Body = $@"
                        Hello,

                        {inviterUsername} invited you to collaborate on a task.

                        Task:
                        {taskTitle}

                        You can review the task using the link below:

                        {invitationUrl}

                        This invitation may expire after a certain period.

                        TaskTracker
                        ",
                IsBodyHtml = false
            };

            await client.SendMailAsync(message);
        }

        public async Task SendVerificationCodeAsync(string email, string code)
        {
            using var client = CreateClient();

            using var message = new MailMessage(_fromEmail, email)
            {
                Subject = "Your verification code",
                Body = $"Your verification code is: {code}",
                IsBodyHtml = false
            };

            await client.SendMailAsync(message);
        }

        public async Task SendPasswordResetCodeAsync(string email, string code)
        {
            using var client = CreateClient();

            using var message = new MailMessage(_fromEmail, email)
            {
                Subject = "TaskTracker - Password reset code",
                Body = $@"Your password reset code is: {code}

This code expires in 10 minutes.

If you did not request a password reset, you can ignore this email.",
                IsBodyHtml = false
            };

            await client.SendMailAsync(message);
        }

        private SmtpClient CreateClient()
        {
            if (_smtpPort is not int port || string.IsNullOrWhiteSpace(_smtpHost) || string.IsNullOrWhiteSpace(_fromEmail))
                throw new InvalidOperationException("SMTP host, port and sender must be configured to send mail.");

            return new SmtpClient(_smtpHost, port)
            {
                Credentials = new NetworkCredential(_smtpUser, _smtpPass),
                EnableSsl = true
            };
        }
    }
}
