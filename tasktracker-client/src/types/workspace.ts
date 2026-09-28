import type { CreateTaskRequest } from "./CreateTaskRequest";

export type WorkspaceRole = "Owner" | "Admin" | "Member";

export type WorkspaceInvitationStatus =
  | "Pending"
  | "Accepted"
  | "Rejected"
  | "Cancelled"
  | "Expired";

export type WorkspaceListItem = {
  id: number;
  name: string;
  role: WorkspaceRole;
  createdAt: string;
};

export type WorkspaceMember = {
  userId: number;
  userName: string;
  role: WorkspaceRole;
  joinedAt: string;
  version: number;
};

export type WorkspaceDetail = {
  id: number;
  name: string;
  createdAt: string;
  version: number;
  currentUserRole: WorkspaceRole;
  memberCount: number;
  members: WorkspaceMember[];
};

export type CreateWorkspaceRequest = {
  name: string;
};

export type WorkspaceInvitation = {
  id: number;
  workspaceId: number;
  workspaceName: string;
  invitedUserId: number;
  invitedUserName: string;
  invitedByUserName: string;
  status: WorkspaceInvitationStatus;
  createdAt: string;
  respondedAt: string | null;
  expiresAt: string | null;
  version: number;
};

export type CreateWorkspaceInvitationRequest = {
  username: string;
};

export type WorkspaceVersionRequest = {
  version: number;
};

export type ChangeWorkspaceMemberRoleRequest = WorkspaceVersionRequest & {
  role: Exclude<WorkspaceRole, "Owner">;
};

export type WorkspaceTaskCreateRequest = CreateTaskRequest & {
  assigneeUserId: number | null;
};
