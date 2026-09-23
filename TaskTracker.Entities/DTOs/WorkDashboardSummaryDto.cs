namespace TaskTracker.Entities.DTOs;

public class WorkDashboardSummaryDto
{
    public int AssignedToMeCount { get; set; }
    public int AwaitingMyReviewCount { get; set; }
    public int OverdueCount { get; set; }
    public int PendingInvitationCount { get; set; }
}
