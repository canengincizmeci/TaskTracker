export interface Task {
  id: number;
  title: string;
  description: string;
  category: string;
  priority: string;
  status: string;
  activity: boolean;
  createdAt: string;
  dueDate: string | null;
  isOwner?: boolean;
  isSharedWithMe?: boolean;
  canView?: boolean;
  canEdit?: boolean;
  canShare?: boolean;
  visibility?: string;
}