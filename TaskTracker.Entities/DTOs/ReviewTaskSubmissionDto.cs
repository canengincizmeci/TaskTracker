using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs;

public class ReviewTaskSubmissionDto
{
    public long Version { get; set; }
    public TaskReviewDecision Decision { get; set; }
    public string? Feedback { get; set; }
}
