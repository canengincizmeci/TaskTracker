import axiosClient from "./axiosClient";
import type { Task } from "../types/task";

async function getAllTasks(): Promise<Task[]> {
  const response = await axiosClient.get("/TaskRequest");
  return response.data;
}

async function getTaskById(id: number): Promise<Task> {
  const response = await axiosClient.get(`/TaskRequest/${id}`);
  return response.data;
}

export { getAllTasks, getTaskById };