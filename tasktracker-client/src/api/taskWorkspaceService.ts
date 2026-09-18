import axiosClient from "./axiosClient";
import type { TaskActivity, TaskMessage } from "../types/taskWorkspace";

export async function getTaskActivity(taskId: number): Promise<TaskActivity[]> {
  return (await axiosClient.get(`/TaskWorkspace/${taskId}/activity`)).data;
}

export async function getTaskMessages(taskId: number): Promise<TaskMessage[]> {
  return (await axiosClient.get(`/TaskWorkspace/${taskId}/messages`)).data;
}

export async function sendTaskMessage(taskId: number, content: string): Promise<TaskMessage> {
  return (await axiosClient.post(`/TaskWorkspace/${taskId}/messages`, { content })).data;
}
