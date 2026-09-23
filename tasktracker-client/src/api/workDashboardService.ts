import axiosClient from "./axiosClient";
import type { WorkDashboardSummary } from "../types/workDashboard";

export async function getWorkDashboardSummary(): Promise<WorkDashboardSummary> {
  return (await axiosClient.get<WorkDashboardSummary>("/work-dashboard/summary")).data;
}
