export interface UpdateTaskRequest {
  id: number;
  title: string;
  description: string;
  category: string;
  priority: string;
  status: string;
  dueDate: string | null;
}
