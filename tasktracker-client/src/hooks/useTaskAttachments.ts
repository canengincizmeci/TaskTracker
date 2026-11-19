import { createContext, useContext, useSyncExternalStore } from "react";
import { isAxiosError } from "axios";
import { getTaskAttachments } from "../api/taskAttachmentService";
import { errorMessage } from "../api/errorMessage";
import type { TaskAttachment } from "../types/taskAttachment";

type FileSnapshot = { files: TaskAttachment[]; loading: boolean; error: string };

/** One authorized list for the mounted workspace, including all three attachment kinds. */
export function createAttachmentCollection(taskId: number, accessScope: string | null, accessRevoked: () => void) {
  let snapshot: FileSnapshot = { files: [], loading: !!accessScope, error: "" };
  let pending: { controller: AbortController; response: Promise<TaskAttachment[]> } | undefined;
  let lastLoaded = 0;
  const listeners = new Set<() => void>();

  function publish(next: FileSnapshot) {
    snapshot = next;
    listeners.forEach((notify) => notify());
  }

  function refresh(): Promise<TaskAttachment[]> {
    if (!accessScope) return Promise.reject(new Error("Files are unavailable."));
    if (pending) return pending.response;
    const controller = new AbortController();
    publish({ ...snapshot, loading: true, error: "" });
    const response = getTaskAttachments(taskId, controller.signal).then((files) => {
      if (!controller.signal.aborted) {
        lastLoaded = Date.now();
        publish({ files, loading: false, error: "" });
      }
      return files;
    }).catch((reason: unknown) => {
      if (!controller.signal.aborted) {
        lastLoaded = 0;
        publish({ files: [], loading: false, error: errorMessage(reason, "Could not load files.") });
        if (isAxiosError(reason) && reason.response?.status === 403) accessRevoked();
      }
      throw reason;
    }).finally(() => {
      if (pending?.controller === controller) pending = undefined;
    });
    pending = { controller, response };
    return response;
  }

  function cancel() {
    pending?.controller.abort();
    pending = undefined;
  }

  return {
    refresh,
    cancel,
    invalidate() {
      // A read started before a committed mutation can still return the previous list.
      cancel();
      return refresh();
    },
    refreshIfStale() {
      if (Date.now() - lastLoaded > 15000) return refresh();
      return Promise.resolve(snapshot.files);
    },
    getSnapshot: () => snapshot,
    subscribe(notify: () => void) {
      listeners.add(notify);
      return () => { listeners.delete(notify); };
    },
  };
}

export const AttachmentContext = createContext<ReturnType<typeof createAttachmentCollection> | null>(null);

export function useTaskAttachments() {
  const store = useContext(AttachmentContext);
  if (!store) throw new Error("A task workspace is required to access its files.");
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot);
  return { ...snapshot, refresh: store.refresh, refreshFresh: store.invalidate };
}
