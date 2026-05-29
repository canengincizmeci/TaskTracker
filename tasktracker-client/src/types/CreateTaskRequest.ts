export interface CreateTaskRequest {
  title: string;
  description: string;
  category: string;
  priority: string;
  status: string;
  dueDate: string | null;
}