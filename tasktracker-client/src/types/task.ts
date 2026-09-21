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
  assigneeUserId: number | null;
  assigneeUserName: string | null;
  version: number;
  isOwner?: boolean;
  isAssignee?: boolean;
  isSharedWithMe?: boolean;
  canView?: boolean;
  canEdit?: boolean;
  canShare?: boolean;
  canDelete?: boolean;
  canViewParticipants?: boolean;
  canManageResponsibility?: boolean;
  currentUserPermission?: string | null;
  visibility?: string;
}
