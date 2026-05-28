using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class UserTaskListDto:IDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Priority { get; set; }
        public string Status { get; set; }
        public DateOnly? DueDate { get; set; }
        public bool IsOwner { get; set; }
        public bool IsSharedWithMe { get; set; }
        public bool CanView { get; set; }
        public bool CanEdit { get; set; }
        public bool CanShare { get; set; }
    }
}
