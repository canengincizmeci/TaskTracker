import { Link } from "react-router-dom";
import type {
  WorkTaskListItem,
  WorkTaskNextAction,
  WorkTaskStatus,
} from "../types/workDashboard";
import { taskStatusLabel } from "../utils/taskDisplay";

const actionLabels: Record<WorkTaskNextAction, string> = {
  Start: "Start task",
  Submit: "Submit work",
  Revise: "Revise & resubmit",
  Review: "Review submitted work",
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
  const relationships = [
    task.isOwned ? "Owned" : null,
    task.isAssigned ? "Assigned to you" : null,
    task.isShared ? "Shared" : null,
  ].filter((relationship): relationship is string => relationship !== null);

  return <article className={`dashboard-work-item dashboard-work-item--${due.state}`}>
    <span className={`dashboard-work-item__signal dashboard-work-item__signal--${task.status.toLowerCase()}`}
      aria-hidden="true" />
    <div className="dashboard-work-item__body">
      <div className="dashboard-work-item__title-row">
        <h3><Link to={detailPath}>{task.title}</Link></h3>
        <span className={`dashboard-work-item__priority dashboard-work-item__priority--${task.priority.toLowerCase()}`}>
          <span aria-hidden="true" />{task.priority}
        </span>
      </div>
      {task.description && <p>{task.description}</p>}
      <div className="dashboard-work-item__context">
        <span className="dashboard-work-item__category">{task.category}</span>
        <span>{relationships.join(" · ") || "Available"}</span>
        <span>{task.assigneeUserName ?? task.ownerUserName}</span>
      </div>
    </div>
    <div className="dashboard-work-item__state">
      <span className={`dashboard-work-badge dashboard-work-badge--${task.status.toLowerCase()}`}>
        {taskStatusLabel(task.status)}
      </span>
      <span className={`dashboard-work-item__due dashboard-work-item__due--${due.state}`}>{due.label}</span>
    </div>
    <Link className="dashboard-work-item__action" to={detailPath}
      aria-label={`${actionLabels[task.nextAction]}: ${task.title}`}>
      <span>{actionLabels[task.nextAction]}</span><span aria-hidden="true">→</span>
    </Link>
  </article>;
}
