import { useCallback, useEffect, useRef, useState } from "react";
import { isAxiosError } from "axios";
import { errorMessage } from "../api/errorMessage";
import { getSubTaskDetails, listSubTasks } from "../api/subTaskService";
import type { SubTask } from "../types/subTask";

export function subTaskResponseStatus(error: unknown) {
  return isAxiosError(error) ? error.response?.status : undefined;
}

export function subTaskMutationError(error: unknown) {
  const status = subTaskResponseStatus(error);
  if (status === 403) return "Your current permissions do not allow this subtask action.";
  if (status === 404) return "This subtask or its parent is no longer available.";
  if (status === 409) return `${errorMessage(error, "The subtask changed.")} Check the latest state before trying again.`;
  if (status == null || status >= 500) return "The server could not confirm this action. Check the latest state before trying again.";
  return errorMessage(error, "Could not save the subtask.");
}

export function subTaskDetailRefreshKey(item: SubTask, taskStatus: string, currentUserPermission?: string | null,
  rosterRevision = 0) {
  return JSON.stringify([
    item.taskRequestId, item.id, item.version, item.status, item.assigneeUserId, item.currentSubmissionId,
    item.title, item.description, item.priority, item.dueDate, item.capabilities,
    taskStatus, currentUserPermission ?? "", rosterRevision,
  ]);
}

export function useSubTaskList(taskId: number, allowed: boolean, liveRevision: number, onAccessRevoked: () => void,
  checkParent: () => Promise<unknown>) {
  const [reloads, setReloads] = useState(0);
  const [result, setResult] = useState<{ key: string; taskId: number; items: SubTask[] | null; error: string }>();
  const key = `${taskId}:${liveRevision}:${reloads}`;
  const reload = useCallback(() => setReloads(count => count + 1), []);

  useEffect(() => {
    if (!allowed) return;
    const request = new AbortController();
    let settled = false;
    listSubTasks(taskId, request.signal).then(items => {
      if (!request.signal.aborted) setResult({ key, taskId, items, error: "" });
    }).catch((reason: unknown) => {
      if (request.signal.aborted) return;
      const status = subTaskResponseStatus(reason);
      setResult({ key, taskId, items: null, error: status === 404 ? "The subtask list was not found." : "Could not load subtasks." });
      if (status === 401 || status === 403) onAccessRevoked();
      if (status === 404) void checkParent().catch(() => undefined);
    }).finally(() => {
      settled = true;
    });
    return () => {
      if (!settled) request.abort();
    };
  }, [allowed, checkParent, key, onAccessRevoked, taskId]);

  return {
    key, reload, items: allowed && result?.taskId === taskId ? result.items : null,
    loading: allowed && result?.key !== key, error: allowed && result?.key === key ? result.error : "",
  };
}

export type SubTaskList = ReturnType<typeof useSubTaskList>;

export function useSubTaskDetails(taskId: number, id: number, refreshKey: string, onMissing: () => void) {
  type Details = Awaited<ReturnType<typeof getSubTaskDetails>>;
  const [reloads, setReloads] = useState(0);
  const [result, setResult] = useState<{ key: string; data?: Details; error?: string }>();
  const reportedMissing = useRef(false);
  const key = `${refreshKey}:${reloads}`;
  const reload = useCallback(() => setReloads(count => count + 1), []);
  useEffect(() => {
    const request = new AbortController();
    let settled = false;
    getSubTaskDetails(taskId, id, request.signal).then(data => {
      if (!request.signal.aborted) {
        reportedMissing.current = false;
        setResult(previous => {
          const priorVersion = previous?.data?.item.id === id ? previous.data.item.version : -1;
          if (priorVersion > data.item.version) return previous;
          return { key, data };
        });
      }
    }).catch((reason: unknown) => {
      if (request.signal.aborted) return;
      const status = subTaskResponseStatus(reason);
      const missing = status === 401 || status === 403 || status === 404;
      setResult({ key, error: missing ? "This subtask is no longer available." : "Could not load subtask details." });
      if (missing && !reportedMissing.current) {
        reportedMissing.current = true;
        onMissing();
      }
    }).finally(() => {
      settled = true;
    });
    return () => {
      if (!settled) request.abort();
    };
  }, [taskId, id, key, onMissing]);

  return { data: result?.data, loading: result?.key !== key,
    error: result?.key === key ? result.error : undefined, reload };
}
