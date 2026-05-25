import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { deleteTask, getAllTasks } from "../api/taskService";
import type { Task } from "../types/task";
import TaskCard from "../components/TaskCard";
import LoadingSpinner from "../components/LoadingSpinner";

function AdminDashboardPage() {
  const navigate = useNavigate();

  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);
  const [actionLoadingId, setActionLoadingId] = useState<number | null>(null);
  const [error, setError] = useState("");

  const loadTasks = async () => {
    try {
      setError("");
      const data = await getAllTasks();
      setTasks(data);
    } catch (error) {
      setError("Tasks could not be loaded.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadTasks();
  }, []);

  const activeCount = tasks.filter((task) => task.status !== "Done").length;

  const completedCount = tasks.filter((task) => task.status === "Done").length;

  const highPriorityCount = tasks.filter(
    (task) => task.priority === "High" || task.priority === "Critical"
  ).length;

  const openCount = tasks.filter((task) => task.status === "Open").length;

  const handleDeleteTask = async (id: number) => {
    const confirmed = window.confirm(
      "Are you sure you want to delete this task?"
    );

    if (!confirmed) return;

    try {
      setActionLoadingId(id);
      setError("");

      await deleteTask(id);
      await loadTasks();
    } catch (error) {
      setError("Task could not be deleted.");
    } finally {
      setActionLoadingId(null);
    }
  };

  const handleLogout = () => {
    sessionStorage.clear();
    navigate("/login");
  };

  if (loading) {
    return (
      <main className="page public-page">
        <LoadingSpinner text="Loading admin dashboard..." />
      </main>
    );
  }

  return (
    <main className="page public-page admin-dashboard-page">
      <section className="admin-topbar">
        <div>
          <p className="eyebrow">ADMIN DASHBOARD</p>
          <h1>System task overview</h1>
          <p>
            Review all tasks created by users, monitor task status and remove
            outdated or invalid records when necessary.
          </p>
        </div>

        <div className="admin-topbar-actions">
          <button className="secondary-button" onClick={loadTasks}>
            Refresh
          </button>

          <button className="secondary-button" onClick={handleLogout}>
            Logout
          </button>
        </div>
      </section>

      <section className="admin-metrics-grid">
        <div className="admin-metric-card">
          <span>Total tasks</span>
          <strong>{tasks.length}</strong>
        </div>

        <div className="admin-metric-card">
          <span>Active</span>
          <strong>{activeCount}</strong>
        </div>

        <div className="admin-metric-card">
          <span>Open</span>
          <strong>{openCount}</strong>
        </div>

        <div className="admin-metric-card">
          <span>High priority</span>
          <strong>{highPriorityCount}</strong>
        </div>

        <div className="admin-metric-card">
          <span>Completed</span>
          <strong>{completedCount}</strong>
        </div>
      </section>

      {error && (
        <section className="admin-error-box">
          <p className="error-message">{error}</p>
        </section>
      )}

      <section className="admin-full-list-card">
        <div className="admin-list-header">
          <div>
            <p className="eyebrow">ALL TASKS</p>
            <h2>User created tasks</h2>
          </div>

          <span>{tasks.length} records</span>
        </div>

        {tasks.length === 0 ? (
          <div className="empty-card">
            <h3>No tasks found</h3>
            <p>When users create tasks, they will appear here.</p>
          </div>
        ) : (
          <div className="admin-task-list">
            {tasks.map((task) => (
              <div key={task.id} className="admin-task-item">
                <TaskCard task={task} />

                <div className="admin-task-actions">
                  <button
                    className="danger-button"
                    onClick={() => handleDeleteTask(task.id)}
                    disabled={actionLoadingId === task.id}
                  >
                    {actionLoadingId === task.id
                      ? "Deleting..."
                      : "Delete task"}
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>
    </main>
  );
}

export default AdminDashboardPage;