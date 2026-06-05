import axiosClient from "./axiosClient";
import type { Notification } from "../types/notification";

async function getUserNotifications(): Promise<Notification[]> {
  const response = await axiosClient.get(
    "/Notification/user-notifications"
  );

  return response.data;
}

async function markAsRead(notificationId: number): Promise<string> {
  const response = await axiosClient.post(
    `/Notification/mark-as-read/${notificationId}`
  );

  return response.data;
}

async function markAllAsRead(): Promise<string> {
  const response = await axiosClient.post(
    "/Notification/mark-all-as-read"
  );

  return response.data;
}

export {
  getUserNotifications,
  markAsRead,
  markAllAsRead
};