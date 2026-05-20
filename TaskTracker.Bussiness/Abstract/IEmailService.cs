using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Bussiness.Abstract
{
    public interface IEmailService
    {
        Task SendVerificationCodeAsync(string email, string code);
    }
}
