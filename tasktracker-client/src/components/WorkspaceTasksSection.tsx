import { useCallback, useEffect, useRef, useState } from "react";
import type { ChangeEvent, FormEvent } from "react";
import { Link } from "react-router-dom";
import { createWorkspaceTask, getWorkspaceTasks } from "../api/workspaceService";
import { errorMessage } from "../api/errorMessage";
import LoadingSpinner from "./LoadingSpinner";
import type { Task } from "../types/task";
import type { WorkspaceDetail, WorkspaceTaskCreateRequest } from "../types/workspace";

const emptyTask: WorkspaceTaskCreateRequest = {
  title: "",
  description: "",
  category: "",
  priority: "Medium",
  status: "Pending",
  dueDate: null,
  assigneeUserId: null,
};

function formatDate(value: string) {
  const date = value.length === 10 ? new Date(`${value}T00:00:00`) : new Date(value);
  return date.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
}

export default function WorkspaceTasksSection({ workspace }: { workspace: WorkspaceDetail }) {
  const canCreate = workspace.currentUserRole === "Owner" || workspace.currentUserRole === "Admin";
  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);
  const [failure, setFailure] = useState("");
  const requestId = useRef(0);

  const [formOpen, setFormOpen] = useState(false);
  const [draft, setDraft] = useState<WorkspaceTaskCreateRequest>(emptyTask);
  const [creating, setCreating] = useState(false);
  const [createFailure, setCreateFailure] = useState("");
  const [createNotice, setCreateNotice] = useState("");
  const createInProgress = useRef(false);

  const invalidateRequests = useCallback(() => {
    requestId.current++;
  }, []);

  const loadTasks = useCallback(async (initial = true) => {
    const currentRequest = ++requestId.current;
    if (initial) setLoading(true);
    setFailure("");

    try {
      const data = await getWorkspaceTasks(workspace.id);
      if (currentRequest === requestId.current) setTasks(data);
      return true;
    } catch (reason: unknown) {
      if (currentRequest === requestId.current) {
        setFailure(errorMessage(reason, "Workspace tasks could not be loaded."));
      }
      return false;
    } finally {
      if (initial && currentRequest === requestId.current) setLoading(false);
    }
  }, [workspace.id]);

  useEffect(() => {
    const request = window.setTimeout(() => {
      void loadTasks();
    }, 0);

    return () => {
      window.clearTimeout(request);
      invalidateRequests();
    };
  }, [invalidateRequests, loadTasks]);

  const handleChange = (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
    const { name, value } = event.target;
    setDraft((current) => ({
      ...current,
      [name]: name === "assigneeUserId"
        ? value ? Number(value) : null
        : name === "dueDate" ? value || null : value,
    }));
    if (createFailure) setCreateFailure("");
    if (createNotice) setCreateNotice("");
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (createInProgress.current) return;

    const request: WorkspaceTaskCreateRequest = {
      ...draft,
      title: draft.title.trim(),
      description: draft.description.trim(),
      category: draft.category.trim(),
    };

    if (request.title.length < 3) {
      setCreateFailure("Title must contain at least 3 characters.");
      return;
    }
    if (request.description.length < 5) {
      setCreateFailure("Description must contain at least 5 characters.");
      return;
    }
    if (!request.category) {
      setCreateFailure("Category is required.");
      return;
    }

    createInProgress.current = true;
    setCreating(true);
    setCreateFailure("");
    setCreateNotice("");

    try {
      await createWorkspaceTask(workspace.id, request);
      setDraft(emptyTask);
      setFormOpen(false);
      const refreshed = await loadTasks(false);
      setCreateNotice(refreshed
        ? "Workspace task created successfully."
        : "The task was created, but the task list could not be refreshed.");
    } catch (reason: unknown) {
      setCreateFailure(errorMessage(reason, "The workspace task could not be created."));
    } finally {
      createInProgress.current = false;
      setCreating(false);
    }
  };

  return (
    <section className="workspace-detail-card workspace-tasks" aria-labelledby="workspace-tasks-title">
      <div className="workspace-section-heading">
        <div>
          <p className="eyebrow">Work</p>
          <h2 id="workspace-tasks-title">Tasks</h2>
        </div>
        <div className="workspace-tasks__heading-actions">
          {!loading && !failure && <span>{tasks.length} total</span>}
          {canCreate && !formOpen && (
            <button className="primary-button" onClick={() => {
              setFormOpen(true);
              setCreateFailure("");
              setCreateNotice("");
            }} type="button">
              Create task
            </button>
          )}
        </div>
      </div>

      {canCreate && formOpen && (
        <form className="workspace-task-form" onSubmit={handleSubmit} aria-busy={creating}>
          <div className="workspace-task-form__header">
            <div>
              <h3>Create a Workspace task</h3>
              <p>You will be the task owner. Assignment can be changed later from task details.</p>
            </div>
            <button className="workspace-text-button" disabled={creating} onClick={() => {
              setFormOpen(false);
              setCreateFailure("");
            }} type="button">
              Close
            </button>
          </div>

          <div className="workspace-task-form__field">
            <label htmlFor="workspace-task-title">Title</label>
            <input id="workspace-task-title" name="title" value={draft.title} maxLength={100}
              onChange={handleChange} disabled={creating} required />
          </div>

          <div className="workspace-task-form__field">
            <label htmlFor="workspace-task-description">Description</label>
            <textarea id="workspace-task-description" name="description" value={draft.description} maxLength={1000}
              onChange={handleChange} disabled={creating} rows={4} required />
          </div>

          <div className="workspace-task-form__grid">
            <div className="workspace-task-form__field">
              <label htmlFor="workspace-task-category">Category</label>
              <input id="workspace-task-category" name="category" value={draft.category}
                onChange={handleChange} disabled={creating} required />
            </div>
            <div className="workspace-task-form__field">
              <label htmlFor="workspace-task-priority">Priority</label>
              <select id="workspace-task-priority" name="priority" value={draft.priority}
                onChange={handleChange} disabled={creating}>
                <option value="Low">Low</option>
                <option value="Medium">Medium</option>
                <option value="High">High</option>
                <option value="Critical">Critical</option>
              </select>
            </div>
            <div className="workspace-task-form__field">
              <label htmlFor="workspace-task-due-date">Due date</label>
              <input id="workspace-task-due-date" name="dueDate" type="date" value={draft.dueDate ?? ""}
                min={new Date().toISOString().slice(0, 10)} onChange={handleChange} disabled={creating} />
            </div>
            <div className="workspace-task-form__field">
              <label htmlFor="workspace-task-assignee">Assignee</label>
              <select id="workspace-task-assignee" name="assigneeUserId"
                value={draft.assigneeUserId ?? ""} onChange={handleChange} disabled={creating}>
                <option value="">Unassigned</option>
                {workspace.members.map((member) => (
                  <option key={member.userId} value={member.userId}>{member.userName} · {member.role}</option>
                ))}
              </select>
            </div>
          </div>

          {createFailure && <p className="workspace-section-alert" role="alert">{createFailure}</p>}
          <div className="workspace-task-form__actions">
            <button className="secondary-button" disabled={creating} onClick={() => setFormOpen(false)} type="button">
              Cancel
            </button>
            <button className="primary-button" disabled={creating} type="submit">
              {creating ? "Creating..." : "Create task"}
            </button>
          </div>
        </form>
      )}

      {createNotice && <p className="workspace-section-notice" role="status">{createNotice}</p>}
      {!formOpen && createFailure && <p className="workspace-section-alert" role="alert">{createFailure}</p>}

      {loading ? (
        <div className="workspace-compact-state"><LoadingSpinner text="Loading workspace tasks..." /></div>
      ) : failure ? (
        <div className="workspace-compact-state workspace-state--error" role="alert">
          <p>{failure}</p>
          <button className="secondary-button" onClick={() => void loadTasks()} type="button">Try again</button>
        </div>
      ) : tasks.length === 0 ? (
        <div className="workspace-empty-row">
          <strong>No Workspace tasks yet</strong>
          <span>{canCreate ? "Create the first task for this Workspace." : "An Owner or Admin can create tasks here."}</span>
        </div>
      ) : (
        <div className="workspace-task-grid">
          {tasks.map((task) => (
            <Link className="utasks-card workspace-task-card" key={task.id} to={`/tasks/task-detail/${task.id}`}>
              <div className="utasks-card__top">
                <span className="utasks-pill utasks-pill--priority">{task.priority}</span>
                <span className="utasks-pill utasks-pill--status">
                  {task.status === "InReview" ? "Waiting for review" : task.status}
                </span>
              </div>
              <h2>{task.title}</h2>
              <p>{task.description}</p>
              <div className="utasks-card__meta">
                <span>{task.category}</span>
                <span>Owner: {task.ownerUserName}</span>
                <span>Assignee: {task.assigneeUserName ?? "Unassigned"}</span>
                {task.dueDate && <span>Due {formatDate(task.dueDate)}</span>}
              </div>
              <div className="utasks-card__footer">
                <span className="utasks-detail-button">View details →</span>
              </div>
            </Link>
          ))}
        </div>
      )}
    </section>
  );
}
