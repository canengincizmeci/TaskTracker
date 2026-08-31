import { HubConnectionBuilder } from "@microsoft/signalr";
import type { Notification } from "../types/notification";
import { getAccessToken } from "../utils/authStorage";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL.replace(/\/api\/?$/, "");

const notificationHubConnection = new HubConnectionBuilder()
  .withUrl(`${apiBaseUrl}/hubs/notifications`, {
    accessTokenFactory: () => getAccessToken() ?? "",
  })
  .withAutomaticReconnect()
  .build();

notificationHubConnection.on(
  "ReceiveNotification",
  (notification: Notification) => {
    console.log("Received notification:", notification);
  }
);

export { notificationHubConnection };
