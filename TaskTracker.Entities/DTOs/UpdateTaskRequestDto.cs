using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Entities.DTOs
{
    public class UpdateTaskRequestDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Priority { get; set; }
        public string Status { get; set; }
        public bool Activity { get; set; }
        public DateTime DueDate { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

