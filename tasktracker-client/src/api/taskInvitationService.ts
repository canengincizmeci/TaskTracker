import axiosClient from "./axiosClient";
import type { TaskInvitation } from "../types/taskInvitation";

async function getMyPendingInvitations(): Promise<TaskInvitation[]> {
  const response = await axiosClient.get(
    "/TaskShare/my-pending-invitations"
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

export {
  getMyPendingInvitations,
  acceptTaskInvitation,
  rejectTaskInvitation,
};