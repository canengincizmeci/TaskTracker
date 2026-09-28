import axiosClient from "./axiosClient";
import type { TaskPermission } from "../types/taskPermission";
import type { TaskActivity, TaskMessage } from "../types/taskCollaboration";

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
