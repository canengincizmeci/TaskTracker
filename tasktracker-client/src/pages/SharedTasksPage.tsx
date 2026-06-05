import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import axiosClient from "../api/axiosClient";
import LoadingSpinner from "../components/LoadingSpinner";

type SharedTask = {
  taskId: number;
  title: string;
  category: string;
  priority?: string;
  status?: string;
  permission: number;
  sharedAt?: string;
};

function SharedTasksPage() {
  const [sharedTasks, setSharedTasks] = useState<SharedTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState("");

  useEffect(() => {
    const loadSharedTasks = async () => {
      try {
        setLoading(true);
        setErrorMessage("");

        const response = await axiosClient.get<SharedTask[]>(
          "/TaskShare/shared-tasks"
        );

        setSharedTasks(response.data);
      } catch (error: any) {
        const data = error.response?.data;

        const message =
          typeof data === "string"
            ? data
            : data?.message
            ? data.message
            : data?.title
            ? data.title
            : "An error occurred while loading shared tasks.";

        setErrorMessage(message);
      } finally {
        setLoading(false);
      }
    };

    loadSharedTasks();
  }, []);

  const getPermissionText = (permission: number) => {
    if (permission === 0) return "View";
    if (permission === 1) return "Edit";

    return "Unknown";
  };

  if (loading) {
    return (
      <main className="page public-page">
        <LoadingSpinner text="Loading shared tasks..." />
      </main>
    );
  }

  return (
    <main className="page public-page">
      <section className="task-detail-layout">
        <div className="task-detail-main">
          <div className="task-detail-header">
            <div>
              <p className="eyebrow">COLLABORATION</p>
              <h1>Shared With Me</h1>
            </div>

            <div className="task-detail-actions">
              <Link to="/tasks/user-tasks" className="secondary-button">
                My Tasks
              </Link>

              <Link to="/tasks/invitations" className="primary-button">
                Invitations
              </Link>
            </div>
          </div>

          <p className="task-detail-description">
            Tasks shared with you by other users will appear here.
          </p>

          {errorMessage && <p className="error-message">{errorMessage}</p>}

          <section className="task-detail-section">
            <div className="task-section-header">
              <div>
                <p className="eyebrow">TASKS</p>
                <h2>Shared tasks</h2>
              </div>
            </div>

            {sharedTasks.length === 0 ? (
              <div className="activity-timeline">
                <div className="timeline-item">
                  <strong>No shared tasks yet</strong>
                  <span>
                    When you accept a task invitation, the task will be listed
                    here.
                  </span>
                </div>
              </div>
            ) : (
              <div className="task-list">
                {sharedTasks.map((task) => (
                  <article key={task.taskId} className="task-card">
                    <div className="task-card-header">
                      <div>
                        <p className="eyebrow">SHARED TASK</p>
                        <h2>{task.title}</h2>
                      </div>

                      <span className="task-category">
                        {getPermissionText(task.permission)}
                      </span>
                    </div>

                    <div className="task-card-meta">
                      {task.category && <span>{task.category}</span>}
                      {task.priority && <span>{task.priority}</span>}
                      {task.status && <span>{task.status}</span>}
                    </div>

                    {task.sharedAt && (
                      <p className="task-card-description">
                        Shared at{" "}
                        {new Date(task.sharedAt).toLocaleDateString("tr-TR")}
                      </p>
                    )}

                    <div className="task-card-actions">
                      <Link
                        to={`/tasks/task-detail/${task.taskId}`}
                        className="secondary-button"
                      >
                        View Details
                      </Link>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>
        </div>

        <aside className="task-detail-sidebar">
          <div className="task-sidebar-card">
            <div className="task-sidebar-header">
              <p className="eyebrow">QUICK ACCESS</p>
              <h2>Workspace links</h2>
            </div>

            <div className="task-sidebar-links">
              <Link to="/tasks/user-tasks">My Tasks</Link>
              <Link to="/tasks/invitations">Invitations</Link>
              <Link to="/notifications">Notifications</Link>
              <Link to="/profile">Profile</Link>
            </div>
          </div>
        </aside>
      </section>
    </main>
  );
}

export default SharedTasksPage;