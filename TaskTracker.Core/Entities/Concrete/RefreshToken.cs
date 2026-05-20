using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Core.Entities.Concrete
{
    public class RefreshToken:IEntity
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public string Token { get; set; } = null!;
       
        public DateTime Expires { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsRevoked { get; set; } = false;
        public User User { get; set; } = null!;
    }
}
