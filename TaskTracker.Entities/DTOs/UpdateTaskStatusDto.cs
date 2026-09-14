using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Entities.DTOs
{
    public class UpdateTaskStatusDto
    {
        public int Id { get; set; }
        public TaskStatus? Status { get; set; }
    }
}
