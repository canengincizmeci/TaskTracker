import type { TaskPermission } from "./taskPermission";

export interface TaskInvitation {
  id: number;
  taskRequestId: number;
  taskTitle: string;
  inviterUserName: string;
  permission: TaskPermission | null;
  status: string;
  canAccept: boolean;
  canReject: boolean;
  unavailableReason: string | null;
  createdAt: string;
  expiresAt?: string | null;
}
