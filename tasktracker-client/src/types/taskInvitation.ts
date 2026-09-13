export interface TaskInvitation {
  id: number;
  taskRequestId: number;
  taskTitle: string;
  inviterUserName: string;
  permission: "View" | "Edit" | "Manage" | number;
  createdAt: string;
  expiresAt?: string | null;
}