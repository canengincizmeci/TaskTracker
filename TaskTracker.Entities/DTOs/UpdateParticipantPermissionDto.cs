using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs;

public class UpdateParticipantPermissionDto
{
    public TaskPermission Permission { get; set; }
    public long Version { get; set; }
}
