import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import type { Dispatch, FormEvent, SetStateAction } from "react";
import { isAxiosError } from "axios";
import { Link, useNavigate, useParams } from "react-router-dom";
import { deleteTask, getTaskById, taskAction, updateTask } from "../api/taskService";
import type { Task } from "../types/task";
import type { UpdateTaskRequest } from "../types/UpdateTaskRequest";
import TaskResponsibility from "../components/TaskResponsibility";
import TaskCollaborationPanel from "../components/TaskCollaborationPanel";
import LoadingSpinner from "../components/LoadingSpinner";
import TaskSubmissionPanel, {
  TaskSubmissionHistory,
} from "../components/TaskSubmissionPanel";
import { useTaskSubmissionWorkflow } from "../hooks/useTaskSubmissionWorkflow";
import { taskStatusLabel } from "../utils/taskDisplay";

type EditDraft = { title: string; description: string; category: string; priority: string; dueDate: string };
type LoadFailure = { kind: "not-found" | "unauthorized" | "general"; message: string };
const TASK_NOT_FOUND_MESSAGE = "Veri bulunamadı";
const TASK_AUTHORIZATION_DENIED_MESSAGE = "Yetkiniz Yok";

function responseMessage(error: unknown): string | undefined {
  if (!isAxiosError(error)) return undefined;
  const data: unknown = error.response?.data;
  if (typeof data === "string") return data.trim();
  if (data && typeof data === "object") {
    const body = data as Record<string, unknown>;
    if (typeof body.message === "string") return body.message.trim();
    if (typeof body.Message === "string") return body.Message.trim();
  }
  return undefined;
}

function getUpdateError(error: unknown, fallback = "Task could not be updated."): string {
  const data: unknown = isAxiosError(error) ? error.response?.data : undefined;
  if (typeof data === "string" && data.trim()) return data;
  if (data && typeof data === "object") {
    const body = data as Record<string, unknown>;
    if (Array.isArray(body.Errors)) {
      for (const failure of body.Errors) {
        if (failure && typeof failure.ErrorMessage === "string" && failure.ErrorMessage.trim()) return failure.ErrorMessage;
      }
    }
    if (typeof body.Message === "string" && body.Message.trim()) return body.Message;
    if (typeof body.title === "string" && body.title.trim()) return body.title;
  }
  return fallback;
}

function getLoadFailure(error: unknown): LoadFailure {
  const status = isAxiosError(error) ? error.response?.status : undefined;
  const message = responseMessage(error);
  if (status === 403 || status === 401) return {
    kind: "unauthorized", message: "You do not have access to this task.",
  };
  if (status === 404) return { kind: "not-found", message: "This task may have been deleted or no longer exists." };
  if (status === 400 && message === TASK_AUTHORIZATION_DENIED_MESSAGE) return {
    kind: "unauthorized", message: "You do not have access to this task.",
  };
  if (status === 400 && message === TASK_NOT_FOUND_MESSAGE) return {
    kind: "not-found", message: "This task may have been deleted or no longer exists.",
  };
  return { kind: "general", message: "Task details could not be loaded." };
}

function formatDate(value: string) {
  const date = value.length === 10 ? new Date(`${value}T00:00:00`) : new Date(value);
  return date.toLocaleDateString();
}

function TaskDetailContent({ task, isEditing, draft, setDraft, isSaving, isUpdatingStatus, isDeleting,
  editError, startEditing, cancelEditing, saveChanges, handleStatusUpdate, handleDeleteTask, refreshTask,
  handleAccessRevoked }: {
  task: Task;
  isEditing: boolean;
  draft: EditDraft | null;
  setDraft: Dispatch<SetStateAction<EditDraft | null>>;
  isSaving: boolean;
  isUpdatingStatus: boolean;
  isDeleting: boolean;
  editError: string;
  startEditing: () => void;
  cancelEditing: () => void;
  saveChanges: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  handleStatusUpdate: (action: "start" | "complete" | "cancel" | "reopen") => Promise<void>;
  handleDeleteTask: () => Promise<void>;
  refreshTask: () => Promise<void>;
  handleAccessRevoked: () => void;
}) {
  const [moreOpen, setMoreOpen] = useState(false);
  const submissionWorkflow = useTaskSubmissionWorkflow(task, refreshTask);
  const busy = isSaving || isUpdatingStatus || isDeleting || submissionWorkflow.busy;
  const backPath = task.workspaceId ? `/workspaces/${task.workspaceId}` : "/dashboard";
  const backLabel = task.workspaceId ? `Back to ${task.workspaceName ?? "workspace"}` : "Back to My Work";

  return <main className="page public-page task-detail-page">
    <section className="task-detail-shell">
      <Link className="task-detail-back" to={backPath}>← {backLabel}</Link>

      <header className="task-detail-hero">
        <div className="task-detail-hero__top">
          <div className="task-detail-badges">
            <span className="task-status">{taskStatusLabel(task.status)}</span>
            <span className={`priority-pill priority-${task.priority.toLowerCase()}`}>{task.priority}</span>
            {task.dueDate && <span className="task-detail-date">Due {formatDate(task.dueDate)}</span>}
          </div>
          <button type="button" className="secondary-button task-more-button" aria-expanded={moreOpen}
            aria-controls="task-more-actions" onClick={() => setMoreOpen((current) => !current)}>
            More actions
          </button>
        </div>

        <h1>{task.title}</h1>
        {task.workspaceId != null && task.workspaceName && <Link className="task-workspace-context"
          to={`/workspaces/${task.workspaceId}`}>{task.workspaceName}</Link>}
        <div className="task-people-inline">
          <span>Owner <strong>{task.ownerUserName}</strong></span>
          <span>Assigned to <strong>{task.assigneeUserName ?? "Unassigned"}</strong></span>
        </div>

        {moreOpen && <div id="task-more-actions" className="task-more-actions">
          <div className="task-more-actions__standard">
            {task.canEdit === true && task.status !== "InReview" && !isEditing && <button type="button"
              className="secondary-button" disabled={busy} onClick={startEditing}>Edit task</button>}
            {task.canShare === true && <Link to={`/tasks/task-share/${task.id}`} className="secondary-button">
              Share and access
            </Link>}
            {task.isOwner === true && (task.status === "Pending" || task.status === "InProgress" || task.status === "InReview") &&
              <button type="button" className="task-cancel-button" disabled={busy}
                onClick={() => void handleStatusUpdate("cancel")}>Cancel task</button>}
            {task.isOwner === true && (task.status === "Completed" || task.status === "Cancelled") &&
              <button type="button" className="secondary-button" disabled={busy}
                onClick={() => void handleStatusUpdate("reopen")}>Reopen task</button>}
          </div>
          {task.canDelete === true && <div className="task-danger-zone">
            <div><strong>Delete task</strong><span>This permanently removes the task from active work.</span></div>
            <button type="button" className="danger-button" disabled={busy} onClick={() => void handleDeleteTask()}>
              {isDeleting ? "Deleting..." : "Delete task"}
            </button>
          </div>}
        </div>}
      </header>

      {editError && <p className="error-message" role="alert">{editError}</p>}

      <TaskSubmissionPanel task={task} workflow={submissionWorkflow}
        onStart={() => void handleStatusUpdate("start")}
        onComplete={() => void handleStatusUpdate("complete")}
        statusBusy={isUpdatingStatus} />

      <section className="task-detail-section task-description-card" aria-labelledby="task-description-title">
        <div className="task-section-header"><div><p className="eyebrow">TASK</p>
          <h2 id="task-description-title">Description</h2></div></div>
        {isEditing && draft ? <form className="auth-form" onSubmit={saveChanges} aria-busy={isSaving}>
          <div className="form-group"><label htmlFor="edit-title">Title</label>
            <input id="edit-title" type="text" value={draft.title} disabled={isSaving}
              onChange={(event) => setDraft({ ...draft, title: event.target.value })} /></div>
          <div className="form-group"><label htmlFor="edit-description">Description</label>
            <textarea id="edit-description" rows={5} value={draft.description} disabled={isSaving}
              onChange={(event) => setDraft({ ...draft, description: event.target.value })} /></div>
          <div className="task-edit-grid">
            <div className="form-group"><label htmlFor="edit-category">Category</label>
              <input id="edit-category" type="text" value={draft.category} disabled={isSaving}
                onChange={(event) => setDraft({ ...draft, category: event.target.value })} /></div>
            <div className="form-group"><label htmlFor="edit-priority">Priority</label>
              <select id="edit-priority" value={draft.priority} disabled={isSaving}
                onChange={(event) => setDraft({ ...draft, priority: event.target.value })}>
                <option value="Low">Low</option><option value="Medium">Medium</option>
                <option value="High">High</option><option value="Critical">Critical</option>
              </select></div>
            <div className="form-group"><label htmlFor="edit-due-date">Due date</label>
              <input id="edit-due-date" type="date" value={draft.dueDate} disabled={isSaving}
                onChange={(event) => setDraft({ ...draft, dueDate: event.target.value })} /></div>
          </div>
          <div className="task-detail-actions"><button type="button" className="secondary-button" disabled={isSaving}
            onClick={cancelEditing}>Cancel</button><button type="submit" className="primary-button" disabled={isSaving}>
              {isSaving ? "Saving..." : "Save changes"}</button></div>
        </form> : <p className="task-detail-description">{task.description}</p>}
      </section>

      {task.canViewParticipants && <>
        <TaskCollaborationPanel key={`collaboration-${task.id}`} taskId={task.id} onTaskChanged={refreshTask}
          onAccessRevoked={handleAccessRevoked}
          middleContent={<TaskResponsibility key={`assignment-${task.id}-${task.version}`} task={task} onChanged={refreshTask} />}
          historyContent={<TaskSubmissionHistory task={task} workflow={submissionWorkflow} />} />
      </>}
    </section>
  </main>;
}

export default function TaskDetailPage() {
  const { taskId } = useParams();
  const navigate = useNavigate();
  const [task, setTask] = useState<Task | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadFailure, setLoadFailure] = useState<LoadFailure | null>(null);
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState<EditDraft | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [isUpdatingStatus, setIsUpdatingStatus] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [editError, setEditError] = useState("");
  const routeVersion = useRef(0);
  const currentRouteTaskId = useRef(taskId);
  const saving = useRef(false);
  useLayoutEffect(() => { currentRouteTaskId.current = taskId; }, [taskId]);

  const refreshTask = useCallback(async () => {
    if (!taskId) return;
    const requestedTaskId = taskId;
    const route = routeVersion.current;
    const refreshed = await getTaskById(Number(requestedTaskId));
    if (currentRouteTaskId.current === requestedTaskId && routeVersion.current === route) setTask(refreshed);
  }, [taskId]);
  const handleAccessRevoked = useCallback(() => { navigate("/dashboard", { replace: true }); }, [navigate]);

  const loadTask = useCallback(async () => {
    const requestedTaskId = taskId;
    const version = ++routeVersion.current;
    const isCurrent = () => currentRouteTaskId.current === requestedTaskId && routeVersion.current === version;
    setLoading(true); setLoadFailure(null);
    try {
      if (!requestedTaskId || !Number.isSafeInteger(Number(requestedTaskId)) || Number(requestedTaskId) <= 0) {
        if (isCurrent()) setLoadFailure({ kind: "not-found", message: "This task address is invalid." });
        return;
      }
      const data = await getTaskById(Number(requestedTaskId));
      if (isCurrent()) setTask(data);
    } catch (error) {
      if (isCurrent()) { setTask(null); setLoadFailure(getLoadFailure(error)); }
    } finally { if (isCurrent()) setLoading(false); }
  }, [taskId]);

  useEffect(() => {
    const request = window.setTimeout(() => {
      setTask(null); setIsEditing(false); setDraft(null); setEditError("");
      setIsSaving(false); setIsUpdatingStatus(false); setIsDeleting(false); saving.current = false;
      void loadTask();
    }, 0);
    return () => window.clearTimeout(request);
  }, [loadTask]);

  const startEditing = () => {
    if (!task || task.canEdit !== true || saving.current) return;
    setDraft({ title: task.title, description: task.description, category: task.category,
      priority: task.priority, dueDate: task.dueDate ?? "" });
    setEditError(""); setIsEditing(true);
  };
  const cancelEditing = () => { if (!saving.current) { setDraft(null); setEditError(""); setIsEditing(false); } };

  const saveChanges = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!task || task.canEdit !== true || !isEditing || !draft || saving.current) return;
    const payload: UpdateTaskRequest = { id: task.id, title: draft.title, description: draft.description,
      category: draft.category, priority: draft.priority, version: task.version, dueDate: draft.dueDate || null };
    const route = routeVersion.current;
    const isCurrent = () => routeVersion.current === route;
    saving.current = true; setIsSaving(true); setEditError("");
    try {
      await updateTask(payload);
      if (!isCurrent()) return;
      try {
        const refreshed = await getTaskById(task.id);
        if (!isCurrent()) return;
        setTask(refreshed);
      } catch {
        if (!isCurrent()) return;
        setTask({ ...task, ...payload }); setEditError("Changes were saved, but task details could not be refreshed.");
      }
      setDraft(null); setIsEditing(false);
    } catch (error) { if (isCurrent()) setEditError(getUpdateError(error)); }
    finally { if (isCurrent()) { saving.current = false; setIsSaving(false); } }
  };

  const handleStatusUpdate = async (action: "start" | "complete" | "cancel" | "reopen") => {
    if (!task || isEditing || saving.current) return;
    const snapshot = task;
    const route = routeVersion.current;
    const isCurrent = () => routeVersion.current === route;
    saving.current = true; setIsUpdatingStatus(true); setEditError("");
    try {
      await taskAction(snapshot.id, action, snapshot.version);
      if (!isCurrent()) return;
      try {
        const refreshed = await getTaskById(snapshot.id);
        if (isCurrent()) setTask(refreshed);
      } catch { if (isCurrent()) setEditError("The action succeeded, but task details could not be refreshed."); }
    } catch (error) { if (isCurrent()) setEditError(getUpdateError(error)); }
    finally { if (isCurrent()) { saving.current = false; setIsUpdatingStatus(false); } }
  };

  const handleDeleteTask = async () => {
    if (!task || task.canDelete !== true || isEditing || saving.current) return;
    if (!window.confirm("Delete this task? This action cannot be undone.")) return;
    const route = routeVersion.current;
    const isCurrent = () => routeVersion.current === route;
    saving.current = true; setIsDeleting(true); setEditError("");
    try {
      await deleteTask(task.id);
      if (isCurrent()) navigate(task.workspaceId ? `/workspaces/${task.workspaceId}` : "/dashboard", { replace: true });
    } catch (error) { if (isCurrent()) setEditError(getUpdateError(error, "Task could not be deleted.")); }
    finally { if (isCurrent()) { saving.current = false; setIsDeleting(false); } }
  };

  if (loading || (task && task.id !== Number(taskId))) return <main className="page public-page"><LoadingSpinner text="Loading task details..." /></main>;
  if (!task) return <main className="page public-page"><section className="task-detail-not-found" role="alert">
    <h1>{loadFailure?.kind === "unauthorized" ? "Task unavailable" : loadFailure?.kind === "not-found" ? "Task not found" : "Could not load task"}</h1>
    <p>{loadFailure?.message ?? "Task details could not be loaded."}</p>
    <div className="task-detail-actions">
      {loadFailure?.kind === "general" && <button type="button" className="primary-button" onClick={() => void loadTask()}>Retry</button>}
      <Link to="/dashboard" className="secondary-button">Back to My Work</Link>
    </div>
  </section></main>;

  return <TaskDetailContent key={task.id} task={task} isEditing={isEditing} draft={draft} setDraft={setDraft}
    isSaving={isSaving} isUpdatingStatus={isUpdatingStatus} isDeleting={isDeleting} editError={editError}
    startEditing={startEditing} cancelEditing={cancelEditing} saveChanges={saveChanges}
    handleStatusUpdate={handleStatusUpdate} handleDeleteTask={handleDeleteTask} refreshTask={refreshTask}
    handleAccessRevoked={handleAccessRevoked} />;
}
