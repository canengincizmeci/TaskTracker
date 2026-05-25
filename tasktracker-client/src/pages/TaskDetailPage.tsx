import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getTaskById } from "../api/taskService";
import type { Task } from "../types/task";
import LoadingSpinner from "../components/LoadingSpinner";

function TaskDetailPage() {
  const { id } = useParams();

  const [task, setTask] = useState<Task | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadTask = async () => {
      try {
        if (!id) return;

        const data = await getTaskById(Number(id));
        setTask(data);
      } catch (error) {
        // console.log(error);
      } finally {
        setLoading(false);
      }
    };

    loadTask();
  }, [id]);

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

          <Link to="/" className="primary-button">
            Return Home
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
                  task.priority === "Critical" ||
                  task.priority === "High"
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
              <button className="secondary-button">
                Share Task
              </button>

              <button className="primary-button">
                Edit Task
              </button>
            </div>
          </div>

          <h1>{task.title}</h1>

          <p className="task-detail-description">
            {task.description}
          </p>

          <section className="task-detail-section">
            <div className="task-section-header">
              <div>
                <p className="eyebrow">COLLABORATION</p>
                <h2>Shared users</h2>
              </div>
            </div>

            <div className="shared-users-list">
              <div className="shared-user-card">
                <div className="shared-user-avatar">
                  <span>CE</span>
                </div>

                <div>
                  <strong>Can Engin</strong>
                  <span>Owner</span>
                </div>
              </div>

              <div className="shared-user-card">
                <div className="shared-user-avatar secondary-avatar">
                  <span>AK</span>
                </div>

                <div>
                  <strong>Ahmet Kaya</strong>
                  <span>Edit access</span>
                </div>
              </div>
            </div>
          </section>

          <section className="task-detail-section">
            <div className="task-section-header">
              <div>
                <p className="eyebrow">ACTIVITY</p>
                <h2>Recent activity</h2>
              </div>
            </div>

            <div className="activity-timeline">
              <div className="timeline-item">
                <strong>Task created</strong>
                <span>
                  The task was added to the workspace.
                </span>
              </div>

              <div className="timeline-item">
                <strong>Status updated</strong>
                <span>
                  Task status changed to {task.status}.
                </span>
              </div>

              <div className="timeline-item">
                <strong>Collaboration enabled</strong>
                <span>
                  Shared task structure is ready for future updates.
                </span>
              </div>
            </div>
          </section>
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

            <div className="task-sidebar-info">
              <span>Created</span>
              <strong>
                {new Date(task.createdAt).toLocaleDateString("tr-TR")}
              </strong>
            </div>

            <div className="task-sidebar-info">
              <span>Visibility</span>
              <strong>Private</strong>
            </div>
          </div>

          <div className="task-sidebar-card">
            <div className="task-sidebar-header">
              <p className="eyebrow">QUICK ACCESS</p>
              <h2>Workspace links</h2>
            </div>

            <div className="task-sidebar-links">
              <Link to="/">Dashboard</Link>

              <Link to="/profile">Profile</Link>

              <Link to="/shared-tasks">Shared Tasks</Link>

              <Link to="/create-task">Create Task</Link>
            </div>
          </div>
        </aside>
      </section>
    </main>
  );
}

export default TaskDetailPage;