import axiosClient from "./axiosClient";
import type { Task } from "../types/task";
import type { CreateTaskRequest } from "../types/CreateTaskRequest";
import type { UpdateTaskRequest } from "../types/UpdateTaskRequest";
import type { AwaitingReviewTask, SubmissionReview, TaskSubmission } from "../types/taskSubmission";

async function getAllTasks(): Promise<Task[]> {
  const response = await axiosClient.get("/TaskRequest/list-alltasks");
  return response.data;
}

async function getTaskById(id: number): Promise<Task> {
  const response = await axiosClient.get(`/TaskRequest/get-task/${id}`);
  return response.data;
}

async function createTask(data: CreateTaskRequest): Promise<string> {
  const response = await axiosClient.post("/TaskRequest/add-task", data);
  return response.data;
}

async function deleteTask(id: number): Promise<string> {
  const response = await axiosClient.delete(`/TaskRequest/delete-task/${id}`);
  return response.data;
}

async function getUserTasks(): Promise<Task[]> {
  const response = await axiosClient.get("/TaskRequest/list-user-tasks");
  return response.data.filter((task: Task) => task.isOwner === true);
}

async function getAssignedTasks(): Promise<Task[]> {
  return (await axiosClient.get("/TaskRequest/assigned-to-me")).data;
}



async function updateTask(data: UpdateTaskRequest): Promise<string> {
  const response = await axiosClient.post("/TaskRequest/update-task", data);
  return response.data;
}

async function assignTask(taskId: number, assigneeUserId: number, version: number): Promise<string> {
  return (await axiosClient.post(`/TaskRequest/${taskId}/assign`, { assigneeUserId, version })).data;
}

async function taskAction(taskId: number, action: "unassign" | "start" | "complete" | "cancel" | "reopen", version: number): Promise<string> {
  return (await axiosClient.post(`/TaskRequest/${taskId}/${action}`, { version })).data;
}

async function getTaskSubmissions(taskId: number): Promise<TaskSubmission[]> {
  return (await axiosClient.get(`/TaskRequest/${taskId}/submissions`)).data;
}

async function submitTask(taskId: number, version: number, content: string): Promise<TaskSubmission> {
  return (await axiosClient.post(`/TaskRequest/${taskId}/submissions`, { version, content })).data;
}

async function reviewSubmission(taskId: number, submissionId: number, version: number,
  decision: "Approved" | "ChangesRequested", feedback?: string): Promise<SubmissionReview> {
  return (await axiosClient.post(`/TaskRequest/${taskId}/submissions/${submissionId}/review`,
    { version, decision, feedback: feedback || null })).data;
}

async function getAwaitingReview(): Promise<AwaitingReviewTask[]> {
  return (await axiosClient.get("/TaskRequest/awaiting-review")).data;
}

export { getAllTasks, getTaskById, createTask, deleteTask, getUserTasks, getAssignedTasks, updateTask, assignTask,
  taskAction, getTaskSubmissions, submitTask, reviewSubmission, getAwaitingReview };
