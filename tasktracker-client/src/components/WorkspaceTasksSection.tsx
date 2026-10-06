import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { createWorkspaceTask, getWorkspaceTasks } from "../api/workspaceService";
import { errorMessage } from "../api/errorMessage";
import LoadingSpinner from "./LoadingSpinner";
import TaskCreationForm from "./TaskCreationForm";
import type { Task } from "../types/task";
import {
  createEmptyTaskCreationValue,
  type TaskCreationFormValue,
} from "../types/taskCreationForm";
import type { WorkspaceDetail, WorkspaceTaskCreateRequest } from "../types/workspace";

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
  const routeVersion = useRef(0);
  const currentWorkspaceId = useRef(workspace.id);

  const [formOpen, setFormOpen] = useState(false);
  const [draft, setDraft] = useState<TaskCreationFormValue>(createEmptyTaskCreationValue);
  const [creating, setCreating] = useState(false);
  const [createFailure, setCreateFailure] = useState("");
  const [createNotice, setCreateNotice] = useState("");
  const createInProgress = useRef(false);

  useLayoutEffect(() => {
    if (currentWorkspaceId.current === workspace.id) return;
    currentWorkspaceId.current = workspace.id;
    routeVersion.current++;
    requestId.current++;
  }, [workspace.id]);

  const invalidateRequests = useCallback(() => {
    requestId.current++;
    routeVersion.current++;
  }, []);

  const loadTasks = useCallback(async (initial = true) => {
    const requestedWorkspaceId = workspace.id;
    const route = routeVersion.current;
    const isCurrentWorkspace = () => currentWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;
    if (!isCurrentWorkspace()) return false;
    const currentRequest = ++requestId.current;
    const isCurrent = () => isCurrentWorkspace() && currentRequest === requestId.current;
    if (initial && isCurrent()) setLoading(true);
    setFailure("");

    try {
      const data = await getWorkspaceTasks(requestedWorkspaceId);
      if (isCurrent()) setTasks(data);
      return isCurrent();
    } catch (reason: unknown) {
      if (isCurrent()) {
        setFailure(errorMessage(reason, "Workspace tasks could not be loaded."));
      }
      return false;
    } finally {
      if (initial && isCurrent()) setLoading(false);
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

  const handleSubmit = async (value: TaskCreationFormValue) => {
    if (createInProgress.current) return;
    const requestedWorkspaceId = workspace.id;
    const route = routeVersion.current;
    const isCurrent = () => currentWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;

    const request: WorkspaceTaskCreateRequest = {
      title: value.title,
      description: value.description,
      category: value.category,
      priority: value.priority,
      dueDate: value.dueDate,
      assigneeUserId: value.assigneeUserId,
    };

    createInProgress.current = true;
    setCreating(true);
    setCreateFailure("");
    setCreateNotice("");

    try {
      await createWorkspaceTask(requestedWorkspaceId, request);
      if (!isCurrent()) return;
      setDraft(createEmptyTaskCreationValue());
      setFormOpen(false);
      const refreshed = await loadTasks(false);
      if (!isCurrent()) return;
      setCreateNotice(refreshed
        ? "Workspace task created successfully."
        : "The task was created, but the task list could not be refreshed.");
    } catch (reason: unknown) {
      if (isCurrent()) setCreateFailure(errorMessage(reason, "The workspace task could not be created."));
    } finally {
      if (isCurrent()) {
        createInProgress.current = false;
        setCreating(false);
      }
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
        <TaskCreationForm
          idPrefix={`workspace-${workspace.id}-task`}
          value={draft}
          onChange={(value) => {
            setDraft(value);
            if (createFailure) setCreateFailure("");
            if (createNotice) setCreateNotice("");
          }}
          onSubmit={handleSubmit}
          onCancel={() => {
            if (!creating) {
              setFormOpen(false);
              setCreateFailure("");
            }
          }}
          submitting={creating}
          error={createFailure}
          onClearError={() => setCreateFailure("")}
          assignees={workspace.members.map((member) => ({
            id: member.userId,
            label: `${member.userName} · ${member.role}`,
          }))}
          heading="Create a workspace task"
          description="You will be the task owner. Assignment can be changed later from task details."
          embedded
        />
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
          <strong>No workspace tasks yet</strong>
          <span>{canCreate ? "Create the first task for this workspace." : "An Owner or Admin can create tasks here."}</span>
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
