namespace TaskTracker.Bussiness.Constanst;

public static class WorkspaceMessages
{
    public const string NotFound = "Workspace not found.";
    public const string MemberNotFound = "Workspace member not found.";
    public const string InvitationNotFound = "Workspace invitation not found.";
    public const string NameRequired = "Workspace name is required and cannot exceed 150 characters.";
    public const string UsernameRequired = "Username is required.";
    public const string CannotInviteSelf = "You cannot invite yourself to a workspace.";
    public const string AlreadyMember = "This user is already an active workspace member.";
    public const string PendingInvitationExists = "A pending workspace invitation already exists for this user.";
    public const string InvitationExpired = "This workspace invitation has expired.";
    public const string InvitationAlreadyResponded = "This workspace invitation has already been resolved.";
    public const string OwnerCannotBeRemoved = "The workspace Owner cannot be removed.";
    public const string OwnerCannotChangeOwnRole = "The workspace Owner role cannot be changed.";
    public const string InvalidRoleChange = "Only Member to Admin or Admin to Member role changes are allowed.";
    public const string MembershipInactive = "The workspace membership is already inactive.";
    public const string ConcurrentChange = "The workspace changed. Refresh and retry.";
    public const string ConflictingState = "The workspace state changed or conflicts with this operation. Refresh and retry.";
    public const string Created = "Workspace created successfully.";
    public const string Renamed = "Workspace renamed successfully.";
    public const string InvitationCreated = "Workspace invitation created successfully.";
    public const string InvitationAccepted = "Workspace invitation accepted successfully.";
    public const string InvitationRejected = "Workspace invitation rejected successfully.";
    public const string InvitationCancelled = "Workspace invitation cancelled successfully.";
    public const string MemberRemoved = "Workspace member removed successfully.";
    public const string RoleChanged = "Workspace member role changed successfully.";
}
