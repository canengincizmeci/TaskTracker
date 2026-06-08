using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs
{
    public class SharedTaskDto
    {
        public int TaskId { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public TaskPermission Permission { get; set; }
        public DateTime? SharedAt { get; set; }
    }
}
