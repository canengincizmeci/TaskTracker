export type WorkspaceRole = "Owner" | "Admin" | "Member";

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
