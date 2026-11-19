import axiosClient from "./axiosClient";
import type { TaskAttachment } from "../types/taskAttachment";

export async function getTaskAttachments(taskId: number, signal?: AbortSignal): Promise<TaskAttachment[]> {
  return (await axiosClient.get(`/TaskAttachment/task/${taskId}`, { signal })).data;
}

export const attachmentChangeEvent = "task-attachments-changed";

async function upload(path: string, file: File): Promise<TaskAttachment> {
  const data = new FormData();
  data.append("file", file);
  const result = (await axiosClient.post<TaskAttachment>(path, data)).data;
  window.dispatchEvent(new CustomEvent(attachmentChangeEvent, { detail: result.taskRequestId }));
  return result;
}
export const uploadTaskAttachment = (taskId: number, file: File) => upload(`/TaskAttachment/task/${taskId}`, file);
export const uploadMessageAttachment = (taskId: number, messageId: number, file: File) =>
  upload(`/TaskAttachment/task/${taskId}/messages/${messageId}`, file);
export const uploadSubmissionAttachment = (taskId: number, submissionId: number, file: File) =>
  upload(`/TaskAttachment/task/${taskId}/submissions/${submissionId}`, file);
export async function deleteTaskAttachment(file: TaskAttachment) {
  await axiosClient.delete(`/TaskAttachment/${file.id}`);
  window.dispatchEvent(new CustomEvent(attachmentChangeEvent, { detail: file.taskRequestId }));
}
export async function downloadTaskAttachment(file: TaskAttachment) {
  const response = await axiosClient.get(`/TaskAttachment/${file.id}/download`, { responseType: "blob" });
  const url = URL.createObjectURL(response.data); const link = document.createElement("a");
  link.href = url; link.download = file.fileName; document.body.append(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
