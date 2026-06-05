export interface TaskInvitation {
  id: number;
  taskRequestId: number;
  taskTitle: string;
  inviterUserName: string;
  permission: number;
  createdAt: string;
  expiresAt?: string | null;
}