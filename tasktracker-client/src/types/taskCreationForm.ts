import type { CreateTaskRequest } from "./CreateTaskRequest";

export type TaskCreationFormValue = CreateTaskRequest & {
  mlConsentGranted: boolean;
  assigneeUserId: number | null;
};

export function createEmptyTaskCreationValue(): TaskCreationFormValue {
  return {
    title: "",
    description: "",
    category: "",
    priority: "Medium",
    dueDate: null,
    mlConsentGranted: false,
    assigneeUserId: null,
  };
}
