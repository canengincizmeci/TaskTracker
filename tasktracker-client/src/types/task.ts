export interface Task {
  id: number;
  title: string;
  description: string;
  category: string;
  priority: string;
  status: string;
  activity: boolean;
  createdAt: string;
  dueDate: string | null;
  ownerId: number;
  ownerUserName: string;
  workspaceId?: number | null;
  workspaceName?: string | null;
  assigneeUserId: number | null;
  assigneeUserName: string | null;
  version: number;
  mlConsent?: {
    granted: boolean;
    grantedAt: string | null;
    revokedAt: string | null;
    policyVersion: string | null;
  } | null;
  isOwner?: boolean;
  isAssignee?: boolean;
  isSharedWithMe?: boolean;
  canView?: boolean;
  canEdit?: boolean;
  canShare?: boolean;
  canDelete?: boolean;
  canViewParticipants?: boolean;
  canManageResponsibility?: boolean;
  canStart?: boolean;
  canSubmit?: boolean;
  canReview?: boolean;
  workflowSubmissionId?: number | null;
  currentUserPermission?: string | null;
  visibility?: string;
}
