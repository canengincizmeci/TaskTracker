import { useEffect, useState } from "react";
import { HubConnectionBuilder } from "@microsoft/signalr";
import { getTaskActivity, getTaskMessages } from "../api/taskWorkspaceService";
import { errorMessage } from "../api/errorMessage";
import { getAccessToken } from "../utils/authStorage";
import type { TaskActivity, TaskMessage } from "../types/taskWorkspace";
import TaskActivityTimeline from "./TaskActivityTimeline";
import TaskDiscussion from "./TaskDiscussion";

function mergeHistory<T extends { id: number; createdAt: string }>(current: T[], incoming: T[]): T[] {
  return [...new Map([...current, ...incoming].map((item) => [item.id, item])).values()]
    .sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt) || a.id - b.id).slice(-100);
}

export default function TaskWorkspace({ taskId, onTaskChanged, onAccessRevoked }: {
  taskId: number;
  onTaskChanged?: () => void | Promise<void>;
  onAccessRevoked?: () => void;
}) {
  const [activities, setActivities] = useState<TaskActivity[]>([]);
  const [messages, setMessages] = useState<TaskMessage[]>([]);
  const [activityError, setActivityError] = useState("");
  const [messageError, setMessageError] = useState("");
  const [liveError, setLiveError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let disposed = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const apiBase = import.meta.env.VITE_API_BASE_URL.replace(/\/api\/?$/, "");
    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBase}/hubs/task-workspace`, { accessTokenFactory: () => getAccessToken() ?? "" })
      .withAutomaticReconnect().build();

    const loadHistory = async () => {
      await Promise.all([
        getTaskActivity(taskId).then((items) => {
          if (!disposed) { setActivities((current) => mergeHistory(current, items)); setActivityError(""); }
        }).catch((reason: unknown) => {
          if (!disposed) setActivityError(errorMessage(reason, "Could not load activity."));
        }),
        getTaskMessages(taskId).then((items) => {
          if (!disposed) { setMessages((current) => mergeHistory(current, items)); setMessageError(""); }
        }).catch((reason: unknown) => {
          if (!disposed) setMessageError(errorMessage(reason, "Could not load discussion."));
        }),
      ]);
      if (!disposed) setLoading(false);
    };

    connection.on("TaskActivityCreated", (item: TaskActivity) => {
      if (!disposed) setActivities((current) => mergeHistory(current, [item]));
    });
    connection.on("TaskMessageCreated", (item: TaskMessage) => {
      if (!disposed) setMessages((current) => mergeHistory(current, [item]));
    });
    connection.on("TaskChanged", () => {
      if (!disposed) void Promise.resolve(onTaskChanged?.()).catch(() => {
        if (!disposed) setLiveError("Task changed, but the latest details could not be loaded. Refresh to retry.");
      });
    });
    connection.on("TaskAccessRevoked", async () => {
      if (!disposed) setLiveError("Your access to this task was removed.");
      await connection.stop();
      onAccessRevoked?.();
    });

    const joinAndSync = async () => {
      await connection.invoke("JoinTask", taskId);
      if (disposed) return;
      setLiveError("");
      // Reload after joining/rejoining to recover events missed while disconnected.
      await loadHistory();
    };
    const start = async () => {
      try {
        await connection.start();
        if (disposed) { await connection.stop(); return; }
        await joinAndSync();
      } catch {
        if (disposed) return;
        setLiveError("Live updates unavailable. Saved history remains available; reconnecting...");
        await connection.stop();
        if (!disposed) retry = setTimeout(() => { void start(); }, 5000);
      }
    };
    connection.onreconnecting(() => {
      if (!disposed) setLiveError("Reconnecting live updates...");
    });
    connection.onreconnected(async () => {
      try { await joinAndSync(); }
      catch {
        if (!disposed) setLiveError("Could not rejoin this workspace. Refresh to check access.");
        await connection.stop();
      }
    });
    connection.onclose(() => {
      if (!disposed) setLiveError("Live updates disconnected. Refresh to reconnect; saved history is retained.");
    });
    void loadHistory();
    void start();
    return () => {
      disposed = true;
      if (retry) clearTimeout(retry);
      // Closing this component's connection also leaves its task group.
      void connection.stop();
    };
  }, [taskId, onTaskChanged, onAccessRevoked]);

  return <>
    {liveError && <p role="status">{liveError}</p>}
    <TaskActivityTimeline activities={activities} loading={loading} error={activityError} />
    <TaskDiscussion taskId={taskId} messages={messages} loading={loading} error={messageError}
      onMessage={(message) => setMessages((current) => mergeHistory(current, [message]))} />
  </>;
}
