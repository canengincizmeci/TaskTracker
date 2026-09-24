import { Link } from "react-router-dom";
import type {
  WorkTaskListItem,
  WorkTaskNextAction,
  WorkTaskStatus,
} from "../types/workDashboard";

const statusLabels: Record<WorkTaskStatus, string> = {
  Pending: "Pending",
  InProgress: "In progress",
  InReview: "In review",
  Completed: "Completed",
  Cancelled: "Cancelled",
};

const actionLabels: Record<WorkTaskNextAction, string> = {
  Start: "Start work",
  Submit: "Submit work",
  Revise: "Continue revision",
  Review: "Review submission",
  View: "Open task",
};

function parseDateOnly(value: string) {
  const [year, month, day] = value.split("-").map(Number);
  return new Date(year, month - 1, day);
}

function startOfToday() {
  const today = new Date();
  return new Date(today.getFullYear(), today.getMonth(), today.getDate());
}

function duePresentation(value: string | null, status: WorkTaskStatus) {
  if (!value) return { label: "No due date", state: "none" };

  const due = parseDateOnly(value);
  const today = startOfToday();
  const daysAway = Math.round((due.getTime() - today.getTime()) / 86_400_000);
  const terminal = status === "Completed" || status === "Cancelled";
  const formatted = due.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });

  if (!terminal && daysAway < 0) return { label: `Overdue · ${formatted}`, state: "overdue" };
  if (!terminal && daysAway === 0) return { label: "Due today", state: "today" };
  if (!terminal && daysAway > 0 && daysAway <= 7) return { label: `Due soon · ${formatted}`, state: "soon" };
  return { label: `Due ${formatted}`, state: "future" };
}

export default function DashboardWorkItem({ task }: { task: WorkTaskListItem }) {
  const due = duePresentation(task.dueDate, task.status);
  const detailPath = `/tasks/task-detail/${task.taskId}`;

  return <article className="dashboard-work-item">
    <div className="dashboard-work-item__content">
      <div className="dashboard-work-item__badges">
        <span className={`dashboard-work-badge dashboard-work-badge--status dashboard-work-badge--${task.status.toLowerCase()}`}>
          {statusLabels[task.status]}
        </span>
        <span className={`dashboard-work-badge dashboard-work-badge--priority-${task.priority.toLowerCase()}`}>
          {task.priority}
        </span>
        {task.isOwned && <span className="dashboard-work-badge dashboard-work-badge--relationship">Owned</span>}
        {task.isAssigned && <span className="dashboard-work-badge dashboard-work-badge--relationship">Assigned to you</span>}
        {task.isShared && <span className="dashboard-work-badge dashboard-work-badge--relationship">Shared</span>}
      </div>

      <div>
        <h3><Link to={detailPath}>{task.title}</Link></h3>
        <p className="dashboard-work-item__description">{task.description}</p>
      </div>

      <div className="dashboard-work-item__meta">
        <span>Owner: <strong>{task.ownerUserName}</strong></span>
        <span>Assignee: <strong>{task.assigneeUserName ?? "Unassigned"}</strong></span>
        <span>Category: <strong>{task.category}</strong></span>
        <span className={`dashboard-work-item__due dashboard-work-item__due--${due.state}`}>{due.label}</span>
      </div>
    </div>

    <Link className="primary-button dashboard-work-item__action" to={detailPath}>
      {actionLabels[task.nextAction]}
    </Link>
  </article>;
}
