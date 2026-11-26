import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState, type ReactNode } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { isAxiosError } from "axios";
import { getTaskActivity, getTaskMessages } from "../api/taskCollaborationService";
import { attachmentChangeEvent } from "../api/taskAttachmentService";
import { errorMessage } from "../api/errorMessage";
import { useAuth } from "../context/authContextValue";
import { AttachmentContext, createAttachmentCollection } from "../hooks/useTaskAttachments";
import { ConversationContext, useTaskConversation } from "../hooks/useTaskConversation";
import { getAccessToken } from "../utils/authStorage";
import { decodeJwt } from "../utils/jwtHelper";
import type { Task } from "../types/task";
import type { TaskActivity, TaskMessage } from "../types/taskCollaboration";
import TaskActivityTimeline from "./TaskActivityTimeline";

function mergeHistory<T extends { id: number; createdAt: string }>(current: T[], incoming: T[]): T[] {
  return [...new Map([...current, ...incoming].map((item) => [item.id, item])).values()]
    .sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt) || a.id - b.id).slice(-100);
}

type CollaborationProps = {
  task: Task;
  view?: string;
  onTaskChanged: () => unknown;
  onAccessRevoked: () => void;
  children: ReactNode;
};

export default function TaskCollaborationPanel(props: CollaborationProps) {
  const { user, token } = useAuth();
  const { task } = props;
  const claims = token ? decodeJwt(token) : null;
  const identifier = claims?.["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"];
  const id = Number(identifier);
  const currentUserId = Number.isSafeInteger(id) && id > 0 ? id : null;
  const session = JSON.stringify([task.id, currentUserId, user?.email, user?.role, task.canViewParticipants]);
  return <WorkspaceConnection key={session} currentUserId={currentUserId} {...props} />;
}

function WorkspaceConnection({ task, view, children, onAccessRevoked, onTaskChanged, currentUserId }:
  CollaborationProps & { currentUserId: number | null }) {
  const taskId = task.id;
  const allowed = !!task.canViewParticipants;
  const scope = allowed ? JSON.stringify([task.isOwner, task.currentUserPermission, task.canEdit, task.canShare]) : null;
  const attachments = useMemo(() => createAttachmentCollection(taskId, scope, onAccessRevoked), [taskId, scope, onAccessRevoked]);
  const [activities, setActivities] = useState<TaskActivity[]>([]);
  const [messages, setMessages] = useState<TaskMessage[]>([]);
  const [activityError, setActivityError] = useState("");
  const [messageError, setMessageError] = useState("");
  const [liveError, setLiveError] = useState("");
  const [rosterRevision, setRosterRevision] = useState(0);
  const [taskRevision, setTaskRevision] = useState(0);
  const [loading, setLoading] = useState(allowed);
  const historyRequest = useRef({ sequence: 0 });
  const revoked = useEffectEvent(onAccessRevoked);
  const syncFiles = useEffectEvent(() => attachments.invalidate().catch(() => undefined));
  const syncTask = useEffectEvent(() => {
    setTaskRevision(revision => revision + 1);
    return Promise.resolve(onTaskChanged()).catch(() => undefined);
  });
  const refreshRoster = useEffectEvent((workspaceId: number) => {
    if (workspaceId === task.workspaceId) setRosterRevision(version => version + 1);
  });

  const addMessage = useCallback((message: TaskMessage) => {
    setMessages((current) => mergeHistory(current, [message]));
  }, []);

  const reload = useCallback(async () => {
    if (!allowed) return;
    const request = ++historyRequest.current.sequence;
    const current = () => request === historyRequest.current.sequence;
    setLoading(true);
    await Promise.all([
      getTaskActivity(taskId).then((items) => {
        if (current()) {
          setActivities((saved) => mergeHistory(saved, items));
          setActivityError("");
        }
      }).catch((error: unknown) => {
        if (current()) {
          setActivityError(errorMessage(error, "Could not load activity."));
          if (isAxiosError(error) && error.response?.status === 403) onAccessRevoked();
        }
      }),
      getTaskMessages(taskId).then((items) => {
        if (current()) {
          setMessages((saved) => mergeHistory(saved, items));
          setMessageError("");
        }
      }).catch((error: unknown) => {
        if (current()) {
          setMessageError(errorMessage(error, "Could not load discussion."));
          if (isAxiosError(error) && error.response?.status === 403) onAccessRevoked();
        }
      }),
    ]);
    if (current()) setLoading(false);
  }, [taskId, allowed, onAccessRevoked]);
  const loadSavedHistory = useEffectEvent(reload);

  useEffect(() => {
    if (!allowed) return;
    const update = (event: Event) => {
      if ((event as CustomEvent<number>).detail === taskId) void attachments.invalidate().catch(() => undefined);
    };
    const onVisible = () => {
      if (!document.hidden) {
        void syncTask();
        void attachments.refreshIfStale().catch(() => undefined);
      }
    };
    window.addEventListener(attachmentChangeEvent, update);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      window.removeEventListener(attachmentChangeEvent, update);
      document.removeEventListener("visibilitychange", onVisible);
      attachments.cancel();
    };
  }, [allowed, taskId, attachments]);

  useEffect(() => {
    if (!allowed) return;
    const queued = window.setTimeout(() => { void attachments.refreshIfStale().catch(() => undefined); }, 0);
    return () => window.clearTimeout(queued);
  }, [attachments, allowed, view]);

  useEffect(() => {
    if (!allowed) return;
    let disposed = false;
    const requests = historyRequest.current;
    let retry: ReturnType<typeof setTimeout> | undefined;
    let fileSync: ReturnType<typeof setTimeout> | undefined;
    let taskSync: ReturnType<typeof setTimeout> | undefined;
    const apiBase = import.meta.env.VITE_API_BASE_URL.replace(/\/api\/?$/, "");
    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBase}/hubs/task-workspace`, { accessTokenFactory: () => getAccessToken() ?? "" })
      .configureLogging(LogLevel.None)
      .withAutomaticReconnect().build();

    // Keep saved history usable even when establishing a live connection takes time.
    const initialHistory = setTimeout(() => { if (!disposed) void loadSavedHistory(); }, 700);
    connection.on("TaskActivityCreated", (item: TaskActivity) => {
      if (!disposed) setActivities((current) => mergeHistory(current, [item]));
    });
    connection.on("TaskMessageCreated", (item: TaskMessage) => {
      if (!disposed) addMessage(item);
    });
    connection.on("TaskAttachmentsChanged", () => {
      if (disposed || fileSync !== undefined) return;
      fileSync = setTimeout(() => {
        fileSync = undefined;
        if (!disposed) void syncFiles();
      }, 80);
    });
    connection.on("TaskChanged", (changedTaskId: number) => {
      if (disposed || changedTaskId !== taskId || taskSync !== undefined) return;
      taskSync = setTimeout(() => {
        taskSync = undefined;
        if (!disposed) void syncTask();
      }, 80);
    });
    connection.on("WorkspaceRosterChanged", (workspaceId: number) => {
      if (!disposed) refreshRoster(workspaceId);
    });
    connection.on("TaskAccessRevoked", () => {
      if (!disposed) revoked();
    });

    const join = async (reconnecting: boolean) => {
      await connection.invoke("JoinTask", taskId);
      if (disposed) return;
      clearTimeout(initialHistory);
      setLiveError("");
      // Fetch after joining so events arriving during the request are merged by id.
      if (reconnecting) setRosterRevision(version => version + 1);
      await Promise.all([loadSavedHistory(), syncTask(), reconnecting && syncFiles()]);
    };
    const start = async (reconnecting: boolean) => {
      try {
        await connection.start();
        if (disposed) { await connection.stop(); return; }
        await join(reconnecting);
      } catch {
        if (!disposed) {
          setLiveError("Live connection interrupted. Reconnecting…");
          void syncTask();
          await connection.stop();
          if (!disposed) scheduleRetry();
        }
      }
    };
    function scheduleRetry() {
      clearTimeout(retry);
      retry = setTimeout(() => { if (!disposed) void start(true); }, 5000);
    }
    connection.onreconnecting(() => {
      if (!disposed) setLiveError("Reconnecting live updates…");
    });
    connection.onreconnected(async () => {
      try { await join(true); }
      catch {
        if (!disposed) {
          void syncTask();
          await connection.stop();
        }
      }
    });
    connection.onclose(() => {
      if (!disposed) {
        setLiveError("Live connection interrupted. Reconnecting…");
        scheduleRetry();
      }
    });
    const initialStart = setTimeout(() => { void start(false); }, 0);
    return () => {
      disposed = true;
      ++requests.sequence;
      clearTimeout(initialStart);
      clearTimeout(initialHistory);
      clearTimeout(retry);
      clearTimeout(fileSync);
      clearTimeout(taskSync);
      void connection.stop();
    };
  }, [taskId, allowed, addMessage]);

  return <AttachmentContext.Provider value={attachments}>
    <ConversationContext.Provider value={{
      taskId, currentUserId, activities, messages, loading, liveError, rosterRevision, taskRevision, onAccessRevoked,
      reload, addMessage, messageError, activityError,
    }}>
      {children}
    </ConversationContext.Provider>
  </AttachmentContext.Provider>;
}

export function TaskWorkspaceActivity() {
  const { activities, activityError, liveError, loading, reload } = useTaskConversation();
  return <>
    {liveError && <p role="status" className="tc-status">{liveError}</p>}
    <TaskActivityTimeline activities={activities} error={activityError} loading={loading} />
    {activityError && <button className="tc-action" type="button" onClick={() => void reload()}>Retry activity</button>}
  </>;
}
