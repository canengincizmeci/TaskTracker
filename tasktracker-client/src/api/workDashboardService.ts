import axiosClient from "./axiosClient";
import type {
  PagedWorkTasks,
  WorkDashboardSummary,
  WorkTaskQuery,
} from "../types/workDashboard";

export async function getWorkDashboardSummary(): Promise<WorkDashboardSummary> {
  return (await axiosClient.get<WorkDashboardSummary>("/work-dashboard/summary")).data;
}

export async function getWorkDashboardTasks(query: WorkTaskQuery): Promise<PagedWorkTasks> {
  return (await axiosClient.get<PagedWorkTasks>("/work-dashboard/tasks", { params: query })).data;
}
