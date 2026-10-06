import type { CreateTaskRequest } from "./CreateTaskRequest";

export type TaskCreationFormValue = CreateTaskRequest & {
  assigneeUserId: number | null;
};

export function createEmptyTaskCreationValue(): TaskCreationFormValue {
  return {
    title: "",
    description: "",
    category: "",
    priority: "Medium",
    dueDate: null,
    assigneeUserId: null,
  };
}
