import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import toast from "react-hot-toast";

import type { Task } from "../types/task";
import { getUserTasks } from "../api/taskService";

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

  const goToTaskDetail = (taskId: number) => {
    navigate(`/tasks/task-detail/${taskId}`);
  };

  return (
    <main className="utasks-page">
      <section className="utasks-hero">
        <div className="utasks-hero__content">
          <p className="utasks-hero__eyebrow">Workspace</p>

          <h1>My Tasks</h1>

          <p>
            View, track and manage the tasks you created in your personal
            workspace.
          </p>
        </div>

        <div className="utasks-hero__panel">
          <span>Total Tasks</span>
          <strong>{tasks.length}</strong>

          <button
            className="utasks-create-button"
            onClick={() => navigate("/tasks/create-task")}
          >
            Create Task
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
          <div className="utasks-state">
            <div className="utasks-spinner" />
            <span>Loading tasks...</span>
          </div>
        )}

        {error && (
          <div className="utasks-state utasks-state--error">{error}</div>
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
            >
              Create First Task
            </button>
          </div>
        )}

        {!loading && !error && tasks.length > 0 && (
          <section className="utasks-grid">
            {tasks.map((task) => (
              <article
                key={task.id}
                className="utasks-card"
                onClick={() => goToTaskDetail(task.id)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    goToTaskDetail(task.id);
                  }
                }}
              >
                <div className="utasks-card__top">
                  <span className="utasks-pill utasks-pill--priority">
                    {task.priority}
                  </span>

                  <span className="utasks-pill utasks-pill--status">
                    {task.status}
                  </span>
                </div>

                <h2>{task.title}</h2>

                <p>{task.description}</p>

                <div className="utasks-card__meta">
                  <span>{task.category}</span>

                  {task.dueDate && <span>{task.dueDate}</span>}
                </div>

                <div className="utasks-card__footer">
                  <button
                    className="utasks-detail-button"
                    onClick={(e) => {
                      e.stopPropagation();
                      goToTaskDetail(task.id);
                    }}
                  >
                    View Details →
                  </button>
                </div>
              </article>
            ))}
          </section>
        )}
      </section>
    </main>
  );
}

export default UserTasksPage;