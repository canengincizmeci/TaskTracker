import { useCallback, useEffect, useRef, useState } from "react";
import { isAxiosError } from "axios";
import { Link, useLocation, useParams } from "react-router-dom";
import { getTaskById } from "../api/taskService";
import { errorMessage } from "../api/errorMessage";
import LoadingSpinner from "../components/LoadingSpinner";
import TaskWorkspace from "../components/TaskWorkspace";
import { getWorkspaceView } from "../utils/taskWorkspaceViews";
import TaskCollaborationPanel from "../components/TaskCollaborationPanel";
import { useAuth } from "../context/AuthContext";
import type { Task } from "../types/task";

type Failure = { title: string; message: string; retry?: boolean };

function loadFailure(error: unknown): Failure {
  const status = isAxiosError(error) ? error.response?.status : undefined;
  const message = errorMessage(error, "");
  if (status === 401 || status === 403 || (status === 400 && message === "Yetkiniz Yok")) {
    return { title: "Task unavailable", message: "You no longer have access to this task." };
  }
  if (status === 404 || (status === 400 && message === "Veri bulunamadı")) {
    return { title: "Task not found", message: "This task may have been removed." };
  }
  return { title: "Could not load task", message: "Check your connection and try again.", retry: true };
}

function TaskRoute({ id }: { id: number }) {
  const location = useLocation();
  const [task, setTask] = useState<Task | null>(null);
  const [failure, setFailure] = useState<Failure | null>(null);
  const latest = useRef<Task | null>(null);
  const generation = useRef(0);
  const request = useRef(0);
  const refreshTask = useCallback(async () => {
    const scope = generation.current;
    const seq = ++request.current;
    try {
      const data = await getTaskById(id);
      if (scope !== generation.current) return undefined;
      if (latest.current && (latest.current.version > data.version ||
          (latest.current.version === data.version && seq < request.current))) return latest.current.version;
      latest.current = data;
      setTask(data);
      setFailure(null);
      return data.version;
    } catch (reason) {
      if (scope !== generation.current || seq !== request.current) return undefined;
      const problem = loadFailure(reason);
      if (!problem.retry) {
        generation.current++;
        latest.current = null;
        setTask(null);
      }
      setFailure(problem);
      throw reason;
    }
  }, [id]);

  useEffect(() => {
    const scope = ++generation.current;
    const start = window.setTimeout(() => { void refreshTask().catch(() => undefined); }, 0);
    return () => { window.clearTimeout(start); generation.current = scope + 1; };
  }, [refreshTask]);

  const revoked = useCallback(() => {
    generation.current++;
    latest.current = null;
    setTask(null);
    setFailure({ title: "Access removed", message: "Your access to this task has been removed." });
  }, []);

  if (!task) return <main className="task-detail-page tw-state">
    {failure ? <section role="alert"><h1>{failure.title}</h1><p>{failure.message}</p>
      <div className="tw-state-actions">{failure.retry && <button type="button" className="tw-control"
        onClick={() => { setFailure(null); void refreshTask().catch(() => undefined); }}>Try again</button>}
        <Link to="/dashboard">Back to My Work</Link></div></section> : <LoadingSpinner text="Loading task…" />}
  </main>;

  const view = getWorkspaceView(location, Boolean(task.canViewParticipants));
  return <TaskCollaborationPanel task={task} view={view} onTaskChanged={refreshTask} onAccessRevoked={revoked}>
    <TaskWorkspace task={task} view={view} refreshTask={refreshTask} refreshFailed={Boolean(failure)} />
  </TaskCollaborationPanel>;
}

export default function TaskDetailPage() {
  const { taskId } = useParams();
  const { token } = useAuth();
  const id = Number(taskId);
  if (!Number.isSafeInteger(id) || id <= 0) return <main className="task-detail-page tw-state">
    <section><h1>Task not found</h1><p>This task address is invalid.</p><Link to="/dashboard">Back to My Work</Link></section>
  </main>;
  return <TaskRoute key={`${taskId}:${token}`} id={id} />;
}
