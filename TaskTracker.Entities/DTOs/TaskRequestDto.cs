using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs
{
    public class TaskRequestDto:IDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Priority { get; set; }
        public string? Visibility { get; set; }
        public string Status { get; set; }
        public bool Activity { get; set; }
        public int? OwnerId { get; set; }
        public bool IsOwner { get; set; }
        public bool CanView { get; set; }
        public bool CanEdit { get; set; }
        public bool CanShare { get; set; }
        public bool CanDelete { get; set; }
        public bool CanViewParticipants { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateOnly? DueDate { get; set; }
    }
}
