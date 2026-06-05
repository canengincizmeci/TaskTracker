export interface Notification {
  id: number;
  type: number;
  title: string;
  message: string;
  isRead: boolean;
  relatedEntityId?: number | null;
  redirectUrl?: string | null;
  createdAt: string;
  readAt?: string | null;
}