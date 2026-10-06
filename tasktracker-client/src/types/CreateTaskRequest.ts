export type TaskPriority = "Low" | "Medium" | "High" | "Critical";

export interface CreateTaskRequest {
  title: string;
  description: string;
  category: string;
  priority: TaskPriority;
  dueDate: string | null;
}
