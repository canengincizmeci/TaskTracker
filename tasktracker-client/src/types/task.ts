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
}