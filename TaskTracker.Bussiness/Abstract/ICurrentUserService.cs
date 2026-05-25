using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Bussiness.Abstract
{
    public interface ICurrentUserService
    {
        int UserId { get; }
    }
}
