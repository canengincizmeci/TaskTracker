using System.ComponentModel.DataAnnotations;

namespace TaskTracker.Entities.DTOs;

public record TaskMlConsentDto(bool Granted, DateTime? GrantedAt, DateTime? RevokedAt, string? PolicyVersion);

public class UpdateTaskMlConsentDto
{
    [Required]
    public bool? Granted { get; init; }

    [Required]
    public long? Version { get; init; }
}
