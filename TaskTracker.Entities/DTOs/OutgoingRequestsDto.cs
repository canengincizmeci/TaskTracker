using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class OutgoingRequestsDto:IDto
    {
        public int RequestId { get; set; }
        public int ToUserId { get; set; }
        public string ToUserName { get; set; } = null!;
        public DateTime RequestedAt { get; set; }
        public int Status { get; set; }
    }
}
