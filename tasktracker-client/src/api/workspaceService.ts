import axiosClient from "./axiosClient";
import type {
  CreateWorkspaceRequest,
  WorkspaceDetail,
  WorkspaceListItem,
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
