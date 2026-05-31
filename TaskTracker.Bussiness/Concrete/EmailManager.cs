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
        private readonly int _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPass;
        private readonly string _fromEmail;
        public EmailManager(IConfiguration configuration)
        {

            _smtpHost = configuration["Smtp:Host"]!;
            _smtpPort = int.Parse(configuration["Smtp:Port"]!);
            _smtpUser = configuration["Smtp:User"]!;
            _smtpPass = configuration["Smtp:Pass"]!;
            _fromEmail = configuration["Smtp:FromEmail"]!;
        }

        public async Task SendTaskShareInvitationEmailAsync(string email, string taskTitle, string inviterUsername, string invitationUrl)
        {
            using var client = new SmtpClient(_smtpHost, _smtpPort)
            {
                Credentials = new NetworkCredential(_smtpUser, _smtpPass),
                EnableSsl = true
            };

            var message = new MailMessage(_fromEmail, email)
            {
                Subject = "TaskTracker - New Task Invitation",
                Body = $@"Hello,{inviterUsername} invited you to collaborate on a task.Task:{taskTitle}You can review the invitation using the link below:{invitationUrl}This invitation may expire after a certain period.TaskTracker",
                IsBodyHtml = false
            };

            await client.SendMailAsync(message);
        }

        public async Task SendVerificationCodeAsync(string email, string code)
        {
            using var client = new SmtpClient(_smtpHost, _smtpPort)
            {
                Credentials = new NetworkCredential(_smtpUser, _smtpPass),
                EnableSsl = true
            };

            var message = new MailMessage(_fromEmail, email)
            {
                Subject = "Your verification code",
                Body = $"Your verification code is: {code}",
                IsBodyHtml = false
            };

            await client.SendMailAsync(message);
        }
    }
}
