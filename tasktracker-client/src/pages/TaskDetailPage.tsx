import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { isAxiosError } from "axios";
import { Link, useParams } from "react-router-dom";
import { getTaskById, updateTask } from "../api/taskService";
import type { Task } from "../types/task";
import type { UpdateTaskRequest } from "../types/UpdateTaskRequest";
import LoadingSpinner from "../components/LoadingSpinner";

type EditDraft = {
  title: string;
  description: string;
  category: string;
  priority: string;
  dueDate: string;
};

function getUpdateError(error: unknown): string {
  const data: unknown = isAxiosError(error) ? error.response?.data : undefined;
  if (typeof data === "string" && data.trim()) return data;
  if (data && typeof data === "object") {
    const body = data as Record<string, unknown>;
    if (Array.isArray(body.Errors)) {
      for (const failure of body.Errors) {
        if (failure && typeof failure.ErrorMessage === "string" && failure.ErrorMessage.trim()) {
          return failure.ErrorMessage;
        }
      }
    }
    if (typeof body.Message === "string" && body.Message.trim()) return body.Message;
    if (typeof body.title === "string" && body.title.trim()) return body.title;
  }
  return "Task could not be updated.";
}

function TaskDetailPage() {
  const { taskId } = useParams();

  const [task, setTask] = useState<Task | null>(null);
  const [loading, setLoading] = useState(true);
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState<EditDraft | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [editError, setEditError] = useState("");
  const routeVersion = useRef(0);
  const saving = useRef(false);

  useEffect(() => {
    const version = ++routeVersion.current;
    const isCurrent = () => routeVersion.current === version;
    setLoading(true);
    setTask(null);
    setIsEditing(false);
    setDraft(null);
    setEditError("");
    setIsSaving(false);
    saving.current = false;
    const loadTask = async () => {
      try {
        if (!taskId) {
          setLoading(false);
          return;
        }

        const data = await getTaskById(Number(taskId));
        if (isCurrent()) setTask(data);
      } catch (error) {
        console.error(error);
      } finally {
        if (isCurrent()) setLoading(false);
      }
    };

    loadTask();
    return () => { routeVersion.current++; };
  }, [taskId]);

  const startEditing = () => {
    if (!task || task.id !== Number(taskId) || task.canEdit !== true || saving.current) return;
    setDraft({
      title: task.title,
      description: task.description,
      category: task.category,
      priority: task.priority,
      dueDate: task.dueDate ?? "",
    });
    setEditError("");
    setIsEditing(true);
  };

  const cancelEditing = () => {
    if (saving.current) return;
    setDraft(null);
    setEditError("");
    setIsEditing(false);
  };

  const saveChanges = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!task || task.id !== Number(taskId) || task.canEdit !== true || !isEditing || !draft || saving.current) return;
    const version = routeVersion.current;
    const isCurrent = () => routeVersion.current === version;
    const payload: UpdateTaskRequest = {
      id: task.id,
      title: draft.title,
      description: draft.description,
      category: draft.category,
      priority: draft.priority,
      status: task.status,
      dueDate: draft.dueDate || null,
    };
    saving.current = true;
    setIsSaving(true);
    setEditError("");
    try {
      await updateTask(payload);
      if (!isCurrent()) return;
      try {
        const refreshed = await getTaskById(task.id);
        if (!isCurrent()) return;
        setTask(refreshed);
        setEditError("");
      } catch {
        if (!isCurrent()) return;
        setTask({ ...task, ...payload });
        setEditError("Changes were saved, but task details could not be refreshed.");
      }
      setDraft(null);
      setIsEditing(false);
    } catch (error) {
      if (isCurrent()) setEditError(getUpdateError(error));
    } finally {
      if (isCurrent()) {
        saving.current = false;
        setIsSaving(false);
      }
    }
  };

  if (loading) {
    return (
      <main className="page public-page">
        <LoadingSpinner text="Loading task details..." />
      </main>
    );
  }

  if (!task) {
    return (
      <main className="page public-page">
        <section className="task-detail-not-found">
          <h1>Task not found</h1>

          <p>
            The task may have been deleted or you may not have permission to
            access it.
          </p>

          <Link to="/tasks/user-tasks" className="primary-button">
            Return My Tasks
          </Link>
        </section>
      </main>
    );
  }

  return (
    <main className="page public-page">
      <section className="task-detail-layout">
        <div className="task-detail-main">
          <div className="task-detail-header">
            <div className="task-detail-badges">
              <span className="task-category">{task.category}</span>

              <span
                className={`task-status ${
                  task.status === "In Progress"
                    ? "status-in-progress"
                    : task.status === "Done"
                      ? "status-done"
                      : ""
                }`}
              >
                {task.status}
              </span>

              <span
                className={`priority-pill ${
                  task.priority === "Critical" || task.priority === "High"
                    ? "priority-high"
                    : task.priority === "Medium"
                      ? "priority-medium"
                      : "priority-low"
                }`}
              >
                {task.priority}
              </span>
            </div>

            <div className="task-detail-actions">
              {/* <button type="button" className="secondary-button">
                Share Task
              </button> */}    
              <Link
                to={`/tasks/task-share/${task.id}`}
                className="secondary-button"
              >     
                Share Task
              </Link>

              {task.canEdit === true && !isEditing && (
                <button type="button" className="primary-button" onClick={startEditing}>
                  Edit Task
                </button>
              )}
            </div>
          </div>

          {editError && <p className="error-message" role="alert">{editError}</p>}
          {isEditing && draft ? (
            <form className="auth-form task-detail-section" onSubmit={saveChanges} aria-busy={isSaving}>
              <div className="form-group">
                <label htmlFor="edit-title">Title</label>
                <input id="edit-title" type="text" value={draft.title} disabled={isSaving}
                  onChange={(event) => setDraft({ ...draft, title: event.target.value })} />
              </div>
              <div className="form-group">
                <label htmlFor="edit-description">Description</label>
                <textarea id="edit-description" rows={5} value={draft.description} disabled={isSaving}
                  onChange={(event) => setDraft({ ...draft, description: event.target.value })} />
              </div>
              <div className="form-group">
                <label htmlFor="edit-category">Category</label>
                <input id="edit-category" type="text" value={draft.category} disabled={isSaving}
                  onChange={(event) => setDraft({ ...draft, category: event.target.value })} />
              </div>
              <div className="form-group">
                <label htmlFor="edit-priority">Priority</label>
                <select id="edit-priority" value={draft.priority} disabled={isSaving}
                  onChange={(event) => setDraft({ ...draft, priority: event.target.value })}>
                  <option value="Low">Low</option>
                  <option value="Medium">Medium</option>
                  <option value="High">High</option>
                  <option value="Critical">Critical</option>
                </select>
              </div>
              <div className="form-group">
                <label htmlFor="edit-due-date">Due Date</label>
                <input id="edit-due-date" type="date" value={draft.dueDate} disabled={isSaving}
                  onChange={(event) => setDraft({ ...draft, dueDate: event.target.value })} />
              </div>
              <div className="task-detail-actions">
                <button type="button" className="secondary-button" disabled={isSaving} onClick={cancelEditing}>Cancel</button>
                <button type="submit" className="primary-button" disabled={isSaving}>{isSaving ? "Saving..." : "Save Changes"}</button>
              </div>
            </form>
          ) : (
            <>
              <h1>{task.title}</h1>
              <p className="task-detail-description">{task.description}</p>
            </>
          )}

        </div>

        <aside className="task-detail-sidebar">
          <div className="task-sidebar-card">
            <div className="task-sidebar-header">
              <p className="eyebrow">DETAILS</p>
              <h2>Task information</h2>
            </div>

            <div className="task-sidebar-info">
              <span>Priority</span>
              <strong>{task.priority}</strong>
            </div>

            <div className="task-sidebar-info">
              <span>Status</span>
              <strong>{task.status}</strong>
            </div>

            <div className="task-sidebar-info">
              <span>Category</span>
              <strong>{task.category}</strong>
            </div>

            {task.createdAt && (
              <div className="task-sidebar-info">
                <span>Created</span>
                <strong>
                  {new Date(task.createdAt).toLocaleDateString("tr-TR")}
                </strong>
              </div>
            )}

            {task.dueDate && (
              <div className="task-sidebar-info">
                <span>Due Date</span>
                <strong>
                  {new Date(task.dueDate).toLocaleDateString("tr-TR")}
                </strong>
              </div>
            )}

            <div className="task-sidebar-info">
              <span>Visibility</span>
              <strong>{task.visibility ?? "Private"}</strong>
            </div>
          </div>

          <div className="task-sidebar-card">
            <div className="task-sidebar-header">
              <p className="eyebrow">QUICK ACCESS</p>
              <h2>Workspace links</h2>
            </div>

            <div className="task-sidebar-links">
              <Link to="/tasks/user-tasks">My Tasks</Link>
              <Link to="/profile">Profile</Link>
              <Link to="/tasks/create-task">Create Task</Link>
            </div>
          </div>
        </aside>
      </section>
    </main>
  );
}

export default TaskDetailPage;
