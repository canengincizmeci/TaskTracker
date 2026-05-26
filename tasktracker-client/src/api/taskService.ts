import axiosClient from "./axiosClient";
import type { Task } from "../types/task";
import type { CreateTaskRequest } from "../types/CreateTaskRequest";

async function getAllTasks(): Promise<Task[]> {
  const response = await axiosClient.get("/TaskRequest");
  return response.data;
}

async function getTaskById(id: number): Promise<Task> {
  const response = await axiosClient.get(`/TaskRequest/${id}`);
  return response.data;
}


async function createTask(data: CreateTaskRequest): Promise<Task> {
  const response = await axiosClient.post("/TaskRequest/add-task", data);
  return response.data;
}

async function deleteTask(id: number): Promise<string> {
  const response = await axiosClient.delete(`/TaskRequest/${id}`);
  return response.data;
}

export { getAllTasks, getTaskById, createTask, deleteTask };
