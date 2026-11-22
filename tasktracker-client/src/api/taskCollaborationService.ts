import axiosClient from "./axiosClient";
import type { TaskPermission } from "../types/taskPermission";
import type { DiscussionAttachmentSettings, TaskActivity, TaskMessage } from "../types/taskCollaboration";

export type Participant = {
  userId: number;
  userName: string;
  permission: TaskPermission | null;
  sharedAt: string | null;
  isAssignee: boolean;
};

export type OutgoingInvitation = {
  id: number;
  taskRequestId: number;
  invitedUserName: string;
  permission: TaskPermission | null;
  status: string;
  createdAt: string;
  expiresAt: string | null;
  canCancel: boolean;
  respondedAt: string | null;
  message: string | null;
  responseMessage: string | null;
};

export async function getParticipants(taskId: number): Promise<Participant[]> {
  return (await axiosClient.get(`/TaskShare/task-participants/${taskId}`)).data;
}

export async function getOutgoingInvitations(taskId: number): Promise<OutgoingInvitation[]> {
  return (await axiosClient.get(`/TaskShare/task/${taskId}/outgoing-invitations`)).data;
}

export async function cancelInvitation(invitationId: number, version: number): Promise<void> {
  await axiosClient.post(`/TaskShare/invitations/${invitationId}/cancel`, { version });
}

export async function updateParticipantPermission(taskId: number, userId: number,
  permission: TaskPermission, version: number): Promise<void> {
  await axiosClient.put(`/TaskShare/task/${taskId}/participants/${userId}/permission`, { permission, version });
}

export async function removeParticipant(taskId: number, userId: number, version: number): Promise<void> {
  await axiosClient.post(`/TaskShare/task/${taskId}/participants/${userId}/remove`, { version });
}

export async function getTaskActivity(taskId: number): Promise<TaskActivity[]> {
  return (await axiosClient.get(`/TaskWorkspace/${taskId}/activity`)).data;
}

export async function getTaskMessages(taskId: number): Promise<TaskMessage[]> {
  return (await axiosClient.get(`/TaskWorkspace/${taskId}/messages`)).data;
}

export async function sendTaskMessage(taskId: number, content: string): Promise<TaskMessage> {
  return (await axiosClient.post(`/TaskWorkspace/${taskId}/messages`, { content })).data;
}

export async function getDiscussionAttachmentSettings(taskId: number, signal: AbortSignal): Promise<DiscussionAttachmentSettings> {
  return (await axiosClient.get(`/TaskWorkspace/${taskId}/messages/attachment-settings`, { signal })).data;
}

export async function deliverTaskMessage(taskId: number, clientRequestId: string, content: string, files: File[],
  signal: AbortSignal, onProgress: (percent: number | undefined) => void): Promise<TaskMessage> {
  const data = new FormData();
  data.append("clientRequestId", clientRequestId);
  if (content) data.append("content", content);
  files.forEach(file => data.append("files", file));
  return (await axiosClient.post(`/TaskWorkspace/${taskId}/messages/delivery`, data, {
    signal, onUploadProgress: ({ loaded, total }) => onProgress(total ? Math.min(100, Math.floor(loaded * 100 / total)) : undefined),
  })).data;
}
