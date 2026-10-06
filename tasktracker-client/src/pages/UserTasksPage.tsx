import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import toast from "react-hot-toast";

import type { Task } from "../types/task";
import { getUserTasks } from "../api/taskService";
import { taskStatusLabel } from "../utils/taskDisplay";

type LocationState = {
  successMessage?: string;
};

function UserTasksPage() {
  const navigate = useNavigate();
  const location = useLocation();

  const hasShownSuccessToast = useRef(false);

  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const state = location.state as LocationState | null;

    if (state?.successMessage && !hasShownSuccessToast.current) {
      hasShownSuccessToast.current = true;
      toast.success(state.successMessage);

      navigate(location.pathname, {
        replace: true,
        state: null,
      });
    }
  }, [location, navigate]);

  useEffect(() => {
    const loadTasks = async () => {
      try {
        setLoading(true);
        setError("");

        const data = await getUserTasks();
        setTasks(data);
      } catch (err) {
        console.error(err);
        setError("Tasks could not be loaded.");
        toast.error("Tasks could not be loaded.");
      } finally {
        setLoading(false);
      }
    };

    loadTasks();
  }, []);

  return (
    <main className="utasks-page">
      <section className="utasks-hero">
        <div className="utasks-hero__content">
          <p className="utasks-hero__eyebrow">Workspace</p>

            <h1>Owned Tasks</h1>

          <p>
            View, track and manage the tasks you created.
          </p>
        </div>

        <div className="utasks-hero__panel">
          <span>Total Tasks</span>
          <strong>{tasks.length}</strong>

          <button
            className="utasks-create-button"
            onClick={() => navigate("/tasks/create-task")}
            type="button"
          >
            Create task
          </button>
        </div>
      </section>

      <section className="utasks-content-card">
        <div className="utasks-section-header">
          <div>
            <p className="utasks-section-header__label">Task list</p>
            <h2>Your active workspace</h2>
          </div>

          <span>{tasks.length} task</span>
        </div>

        {loading && (
          <div className="utasks-state" role="status">
            <div className="utasks-spinner" aria-hidden="true" />
            <span>Loading tasks...</span>
          </div>
        )}

        {error && (
          <div className="utasks-state utasks-state--error" role="alert">{error}</div>
        )}

        {!loading && !error && tasks.length === 0 && (
          <div className="utasks-empty">
            <div className="utasks-empty__icon">✓</div>

            <h2>No tasks yet</h2>

            <p>
              You have not created any tasks yet. Start by creating your first
              task.
            </p>

            <button
              className="utasks-create-button"
              onClick={() => navigate("/tasks/create-task")}
              type="button"
            >
              Create first task
            </button>
          </div>
        )}

        {!loading && !error && tasks.length > 0 && (
          <section className="utasks-grid">
            {tasks.map((task) => (
              <Link
                key={task.id}
                className="utasks-card utasks-card--link"
                to={`/tasks/task-detail/${task.id}`}
              >
                <div className="utasks-card__top">
                  <span className="utasks-pill utasks-pill--priority">
                    {task.priority}
                  </span>

                  <span className="utasks-pill utasks-pill--status">
                    {taskStatusLabel(task.status)}
                  </span>
                </div>

                <h2>{task.title}</h2>

                <p>{task.description}</p>

                <div className="utasks-card__meta">
                  <span>{task.category}</span>

                  {task.dueDate && <span>{task.dueDate}</span>}
                </div>

                <div className="utasks-card__footer">
                  <span className="utasks-detail-button">
                    View details →
                  </span>
                </div>
              </Link>
            ))}
          </section>
        )}
      </section>
    </main>
  );
}

export default UserTasksPage;
