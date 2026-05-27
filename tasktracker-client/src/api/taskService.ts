import axiosClient from "./axiosClient";
import type { Task } from "../types/task";
import type { CreateTaskRequest } from "../types/CreateTaskRequest";

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
export { getAllTasks, getTaskById, createTask, deleteTask };
