using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Core.Entities.Concrete
{
    public class User:IEntity
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public string? PhoneNumber { get; set; }
        public byte[] PasswordSalt { get; set; }
        public byte[] PasswordHash { get; set; }
        public bool Status { get; set; }
        public bool IsVerified { get; set; } = false;
        public bool IsPhoneVerified { get; set; }
        public DateTime? PhoneVerifiedAt { get; set; } 
        public DateTime? UserVerifiedAt { get; set; } 
        public ICollection<UserOperationClaim> UserOperationClaims { get; set; } = new List<UserOperationClaim>();
        public ICollection<TaskRequest> OwnedTaskRequests { get; set; } = new List<TaskRequest>();
        public ICollection<TaskShare> SharedTaskRequests { get; set; } = new List<TaskShare>();


    }
}
