import axiosClient from "./axiosClient";
import type { TaskInvitation } from "../types/taskInvitation";
import type { TaskPermission } from "../types/taskPermission";

export async function inviteToTask(taskRequestId: number, username: string, permission: TaskPermission, message: string) {
  await axiosClient.post("/TaskShare/invite-user", { taskRequestId, username, permission, message: message.trim() || null });
}

export async function getTaskInvitation(id: number): Promise<TaskInvitation> {
  return (await axiosClient.get(`/TaskShare/invitation-details/${id}`)).data;
}

async function getMyPendingInvitations(): Promise<TaskInvitation[]> {
  const response = await axiosClient.get(
    "/TaskShare/get-user-invitations"
  );

  return response.data;
}

async function acceptTaskInvitation(invitationId: number): Promise<string> {
  const response = await axiosClient.post(
    `/TaskShare/accept-invitation/${invitationId}`
  );

  return response.data;
}

async function rejectTaskInvitation(invitationId: number): Promise<string> {
  const response = await axiosClient.post(
    `/TaskShare/reject-invitation/${invitationId}`
  );

  return response.data;
}

async function respondToTaskInvitation(invitationId: number, accept: boolean, responseMessage?: string): Promise<string> {
  return (await axiosClient.post(`/TaskShare/invitations/${invitationId}/respond`, {
    accept, responseMessage: responseMessage?.trim() || null,
  })).data;
}

export {
  getMyPendingInvitations,
  acceptTaskInvitation,
  rejectTaskInvitation,
  respondToTaskInvitation,
};
