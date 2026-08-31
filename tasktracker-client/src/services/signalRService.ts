import { HubConnectionBuilder } from "@microsoft/signalr";
import { getAccessToken } from "../utils/authStorage";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL.replace(/\/api\/?$/, "");

const notificationHubConnection = new HubConnectionBuilder()
  .withUrl(`${apiBaseUrl}/hubs/notifications`, {
    accessTokenFactory: () => getAccessToken() ?? "",
  })
  .withAutomaticReconnect()
  .build();

export { notificationHubConnection };
