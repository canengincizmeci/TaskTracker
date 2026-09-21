namespace TaskTracker.Core.Utilities.Enums;

public enum TaskActivityType
{
    TaskCreated = 1,
    UserInvited = 2,
    InvitationAccepted = 3,
    InvitationRejected = 4,
    UserAssigned = 5,
    UserUnassigned = 6,
    WorkStarted = 7,
    SubmissionCreated = 8,
    ChangesRequested = 9,
    SubmissionApproved = 10,
    TaskCompleted = 11,
    TaskCancelled = 12,
    TaskDetailsUpdated = 13,
    TaskReopened = 14,
    ParticipantRemoved = 15,
    ParticipantPermissionChanged = 16,
    InvitationCancelled = 17
}
