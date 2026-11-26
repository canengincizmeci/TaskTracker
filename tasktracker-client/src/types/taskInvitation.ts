import type { TaskPermission } from "./taskPermission";

export interface TaskInvitation {
  id: number;
  taskRequestId: number | null;
  taskTitle: string | null;
  inviterUserName: string | null;
  permission: TaskPermission | null;
  status: string;
  canAccept: boolean;
  canReject: boolean;
  unavailableReason: string | null;
  createdAt: string;
  expiresAt?: string | null;
  respondedAt?: string | null;
  message?: string | null;
  responseMessage?: string | null;
}
