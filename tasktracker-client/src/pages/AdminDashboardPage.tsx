import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { getAllTasks } from "../api/taskService";
import { errorMessage as getErrorMessage } from "../api/errorMessage";
import type { Task } from "../types/task";
import LoadingSpinner from "../components/LoadingSpinner";
import { taskStatusLabel } from "../utils/taskDisplay";

function formatDate(value: string | null) {
  if (!value) return "No due date";
  return new Date(value).toLocaleDateString(undefined, { dateStyle: "medium" });
}

function AdminDashboardPage() {
  const navigate = useNavigate();
  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [hasData, setHasData] = useState(false);
  const [error, setError] = useState("");

  const loadTasks = async (refresh = false) => {
    try {
      if (refresh) setRefreshing(true);
      setError("");
      setTasks(await getAllTasks());
      setHasData(true);
    } catch (reason: unknown) {
      setError(getErrorMessage(reason, "Tasks could not be loaded."));
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    const request = window.setTimeout(() => { void loadTasks(); }, 0);
    return () => window.clearTimeout(request);
  }, []);

  const openCount = tasks.filter((task) => task.status !== "Completed" && task.status !== "Cancelled").length;
  const completedCount = tasks.filter((task) => task.status === "Completed").length;
  const highPriorityCount = tasks.filter((task) => task.priority === "High" || task.priority === "Critical").length;
  const pendingCount = tasks.filter((task) => task.status === "Pending").length;

  const handleLogout = () => {
    sessionStorage.clear();
    navigate("/login");
  };

  if (loading) {
    return <main className="page public-page admin-dashboard-page"><LoadingSpinner text="Loading admin dashboard..." /></main>;
  }

  return (
    <main className="page public-page admin-dashboard-page">
      <div className="admin-dashboard-shell">
        <header className="admin-topbar">
          <div>
            <p className="eyebrow">ADMINISTRATION</p>
            <h1>Task inventory</h1>
            <p>Monitor the current task population and its operational state.</p>
          </div>
          <div className="admin-topbar-actions">
            <button className="secondary-button" onClick={() => void loadTasks(true)} disabled={refreshing} type="button">
              {refreshing ? "Refreshing..." : "Refresh data"}
            </button>
            <button className="secondary-button" onClick={handleLogout} type="button">Logout</button>
          </div>
        </header>

        {hasData && <section className="admin-metrics-grid" aria-label="Task summary">
          <div className="admin-metric-card"><span>Total tasks</span><strong>{tasks.length}</strong></div>
          <div className="admin-metric-card"><span>Open</span><strong>{openCount}</strong></div>
          <div className="admin-metric-card"><span>Not started</span><strong>{pendingCount}</strong></div>
          <div className="admin-metric-card"><span>High priority</span><strong>{highPriorityCount}</strong></div>
          <div className="admin-metric-card"><span>Completed</span><strong>{completedCount}</strong></div>
        </section>}

        {hasData && <p className="admin-scope-note">
          This is a read-only operational inventory. Task ownership and existing access rules continue to control task details and deletion.
        </p>}

        {error && <div className="admin-error-box" role="alert">
          <span>{error}</span>
          <button className="secondary-button" type="button" onClick={() => void loadTasks(true)} disabled={refreshing}>Try again</button>
        </div>}

        {hasData && <section className="admin-full-list-card" aria-labelledby="admin-task-list-title">
          <div className="admin-list-header">
            <div><p className="eyebrow">ACTIVE RECORDS</p><h2 id="admin-task-list-title">All tasks</h2></div>
            <span>{tasks.length} {tasks.length === 1 ? "record" : "records"}</span>
          </div>

          {tasks.length === 0 ? <div className="admin-empty-state">
            <strong>No active tasks</strong><span>Tasks will appear here when users create them.</span>
          </div> : <div className="admin-task-table" role="table" aria-label="All active tasks">
            <div className="admin-task-table__header" role="row">
              <span role="columnheader">Task</span><span role="columnheader">State</span>
              <span role="columnheader">Priority</span><span role="columnheader">Due</span>
            </div>
            {tasks.map((task) => <article className="admin-task-row" role="row" key={task.id}>
              <div className="admin-task-row__identity" role="cell">
                <span>#{task.id} · {task.category}</span><strong>{task.title}</strong>
              </div>
              <div className="admin-task-row__metric" role="cell"><span className="admin-mobile-label">State</span>
                <span className={`admin-status admin-status--${task.status.toLowerCase().replaceAll(" ", "-")}`}>
                {taskStatusLabel(task.status)}</span></div>
              <div className="admin-task-row__metric" role="cell"><span className="admin-mobile-label">Priority</span>
                <span className={`priority-pill priority-${task.priority.toLowerCase()}`}>{task.priority}</span></div>
              <div className="admin-task-row__metric" role="cell"><span className="admin-mobile-label">Due</span>
                <time dateTime={task.dueDate ?? undefined}>{formatDate(task.dueDate)}</time></div>
            </article>)}
          </div>}
        </section>}
      </div>
    </main>
  );
}

export default AdminDashboardPage;
