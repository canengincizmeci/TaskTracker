export interface UpdateTaskRequest {
  id: number;
  title: string;
  description: string;
  category: string;
  priority: string;
  version: number;
  dueDate: string | null;
}
