import axiosClient from "./axiosClient";
import type { Task } from "../types/task";
import type {
  CreateWorkspaceRequest,
  CreateWorkspaceInvitationRequest,
  ChangeWorkspaceMemberRoleRequest,
  RenameWorkspaceRequest,
  WorkspaceInvitation,
  WorkspaceDetail,
  WorkspaceListItem,
  WorkspaceMember,
  WorkspaceRole,
  WorkspaceVersionRequest,
  WorkspaceTaskCreateRequest,
} from "../types/workspace";

export async function getMyWorkspaces(): Promise<WorkspaceListItem[]> {
  return (await axiosClient.get<WorkspaceListItem[]>("/workspaces")).data;
}

export async function getWorkspace(workspaceId: number): Promise<WorkspaceDetail> {
  return (await axiosClient.get<WorkspaceDetail>(`/workspaces/${workspaceId}`)).data;
}

export async function createWorkspace(
  name: string
): Promise<WorkspaceDetail> {
  const request: CreateWorkspaceRequest = { name };
  return (await axiosClient.post<WorkspaceDetail>("/workspaces", request)).data;
}

export async function renameWorkspace(
  workspaceId: number,
  name: string,
  version: number
): Promise<string> {
  const request: RenameWorkspaceRequest = { name, version };
  return (await axiosClient.put<string>(`/workspaces/${workspaceId}/rename`, request)).data;
}

export async function getWorkspaceMembers(workspaceId: number): Promise<WorkspaceMember[]> {
  return (await axiosClient.get<WorkspaceMember[]>(`/workspaces/${workspaceId}/members`)).data;
}

export async function getWorkspaceInvitations(workspaceId: number): Promise<WorkspaceInvitation[]> {
  return (await axiosClient.get<WorkspaceInvitation[]>(`/workspaces/${workspaceId}/invitations`)).data;
}

export async function getMyWorkspaceInvitations(): Promise<WorkspaceInvitation[]> {
  return (await axiosClient.get<WorkspaceInvitation[]>("/workspaces/invitations/my")).data;
}

export async function inviteWorkspaceMember(
  workspaceId: number,
  username: string
): Promise<WorkspaceInvitation> {
  const request: CreateWorkspaceInvitationRequest = { username };
  return (await axiosClient.post<WorkspaceInvitation>(
    `/workspaces/${workspaceId}/invitations`,
    request
  )).data;
}

export async function acceptWorkspaceInvitation(invitationId: number, version: number): Promise<string> {
  const request: WorkspaceVersionRequest = { version };
  return (await axiosClient.post<string>(`/workspaces/invitations/${invitationId}/accept`, request)).data;
}

export async function rejectWorkspaceInvitation(invitationId: number, version: number): Promise<string> {
  const request: WorkspaceVersionRequest = { version };
  return (await axiosClient.post<string>(`/workspaces/invitations/${invitationId}/reject`, request)).data;
}

export async function cancelWorkspaceInvitation(
  workspaceId: number,
  invitationId: number,
  version: number
): Promise<string> {
  const request: WorkspaceVersionRequest = { version };
  return (await axiosClient.post<string>(
    `/workspaces/${workspaceId}/invitations/${invitationId}/cancel`,
    request
  )).data;
}

export async function removeWorkspaceMember(
  workspaceId: number,
  userId: number,
  version: number
): Promise<string> {
  const request: WorkspaceVersionRequest = { version };
  return (await axiosClient.post<string>(
    `/workspaces/${workspaceId}/members/${userId}/remove`,
    request
  )).data;
}

export async function changeWorkspaceMemberRole(
  workspaceId: number,
  userId: number,
  role: Exclude<WorkspaceRole, "Owner">,
  version: number
): Promise<string> {
  const request: ChangeWorkspaceMemberRoleRequest = { role, version };
  return (await axiosClient.post<string>(
    `/workspaces/${workspaceId}/members/${userId}/role`,
    request
  )).data;
}

export async function getWorkspaceTasks(workspaceId: number): Promise<Task[]> {
  return (await axiosClient.get<Task[]>(`/workspaces/${workspaceId}/tasks`)).data;
}

export async function createWorkspaceTask(
  workspaceId: number,
  request: WorkspaceTaskCreateRequest
): Promise<Task> {
  return (await axiosClient.post<Task>(`/workspaces/${workspaceId}/tasks`, request)).data;
}
