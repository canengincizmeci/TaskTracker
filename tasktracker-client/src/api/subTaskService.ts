import axiosClient from "./axiosClient";
import type { SubTask, SubTaskActivity, SubTaskDecision, SubTaskFields, SubTaskSubmission } from "../types/subTask";
import type { TaskAttachment } from "../types/taskAttachment";
import { downloadAttachment } from "./taskAttachmentService";

export type SubTaskFiles = {
  version: number;
  uploadsEnabled: boolean;
  uploadSubmissionId?: number | null;
  maxFiles?: number;
  maxFileSizeBytes?: number;
  maxTotalBytes?: number;
  files: TaskAttachment[];
};

const collection = (taskId: number) => `/TaskRequest/${taskId}/subtasks`;
const resource = (item: Pick<SubTask, "taskRequestId" | "id">) => `${collection(item.taskRequestId)}/${item.id}`;
const withOffset = (fields: SubTaskFields) => ({ ...fields, utcOffsetMinutes: -new Date().getTimezoneOffset() });

export async function listSubTasks(taskId: number, signal: AbortSignal): Promise<SubTask[]> {
  return (await axiosClient.get(collection(taskId), { signal })).data;
}

export async function getSubTaskDetails(taskRequestId: number, id: number, signal: AbortSignal) {
  const path = resource({ taskRequestId, id });
  const config = { signal };
  const [item, submissions, activities, files] = await Promise.all([
    axiosClient.get<SubTask>(path, config),
    axiosClient.get<SubTaskSubmission[]>(`${path}/submissions`, config),
    axiosClient.get<SubTaskActivity[]>(`${path}/activity`, config),
    axiosClient.get<SubTaskFiles>(`${path}/submission-files`, config),
  ]);
  return { item: item.data, submissions: submissions.data, activities: activities.data, files: files.data };
}

export async function downloadSubTaskFile(item: SubTask, file: TaskAttachment) {
  await downloadAttachment(`${resource(item)}/submissions/${file.subTaskSubmissionId}/files/${file.id}/download`, file.fileName);
}

export async function createSubTask(taskId: number, fields: SubTaskFields, assigneeUserId?: number): Promise<SubTask> {
  return (await axiosClient.post(collection(taskId), { ...withOffset(fields), assigneeUserId })).data;
}

export async function updateSubTask(item: SubTask, fields: SubTaskFields): Promise<void> {
  await axiosClient.put(resource(item), { ...withOffset(fields), version: item.version });
}

export async function deleteSubTask(item: SubTask): Promise<void> {
  await axiosClient.delete(resource(item), { params: { version: item.version } });
}

export async function subTaskAction(item: SubTask, action: "start" | "complete" | "cancel" | "reopen" | "unassign"): Promise<void> {
  await axiosClient.post(`${resource(item)}/${action}`, { version: item.version });
}

export async function assignSubTask(item: SubTask, assigneeUserId: number): Promise<void> {
  await axiosClient.post(`${resource(item)}/assign`, { version: item.version, assigneeUserId });
}

export async function submitSubTask(
  item: SubTask,
  clientRequestId: string,
  content: string,
  files: File[],
  signal?: AbortSignal,
  onProgress?: (percent: number | undefined) => void
): Promise<SubTask> {
  const data = new FormData();
  data.append("version", String(item.version));
  data.append("clientRequestId", clientRequestId);
  if (content) data.append("content", content);
  files.forEach(file => data.append("files", file));
  return (await axiosClient.post<SubTask>(`${resource(item)}/submissions/delivery`, data, {
    signal,
    onUploadProgress: onProgress
      ? ({ loaded, total }) => onProgress(total ? Math.min(100, Math.floor(loaded * 100 / total)) : undefined)
      : undefined,
  })).data;
}

export async function reviewSubTask(item: SubTask, decision: SubTaskDecision, feedback: string): Promise<void> {
  if (item.currentSubmissionId == null) throw new Error("There is no current subtask submission to review.");
  await axiosClient.post(`${resource(item)}/submissions/${item.currentSubmissionId}/review`,
    { version: item.version, decision, feedback: feedback.trim() || null });
}
