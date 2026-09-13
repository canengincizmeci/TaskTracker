import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getTaskById } from "../api/taskService";
import type { Task } from "../types/task";
import LoadingSpinner from "../components/LoadingSpinner";

function TaskDetailPage() {
  const { taskId } = useParams();

  const [task, setTask] = useState<Task | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadTask = async () => {
      try {
        if (!taskId) {
          setLoading(false);
          return;
        }

        const data = await getTaskById(Number(taskId));
        setTask(data);
      } catch (error) {
        console.error(error);
      } finally {
        setLoading(false);
      }
    };

    loadTask();
  }, [taskId]);

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

          <Link to="/tasks/user-tasks" className="primary-button">
            Return My Tasks
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
                  task.priority === "Critical" || task.priority === "High"
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
              {/* <button type="button" className="secondary-button">
                Share Task
              </button> */}    
              <Link
                to={`/tasks/task-share/${task.id}`}
                className="secondary-button"
              >     
                Share Task
              </Link>

              <button type="button" className="primary-button">
                Edit Task
              </button>
            </div>
          </div>

          <h1>{task.title}</h1>

          <p className="task-detail-description">{task.description}</p>

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

            {task.createdAt && (
              <div className="task-sidebar-info">
                <span>Created</span>
                <strong>
                  {new Date(task.createdAt).toLocaleDateString("tr-TR")}
                </strong>
              </div>
            )}

            {task.dueDate && (
              <div className="task-sidebar-info">
                <span>Due Date</span>
                <strong>
                  {new Date(task.dueDate).toLocaleDateString("tr-TR")}
                </strong>
              </div>
            )}

            <div className="task-sidebar-info">
              <span>Visibility</span>
              <strong>{task.visibility ?? "Private"}</strong>
            </div>
          </div>

          <div className="task-sidebar-card">
            <div className="task-sidebar-header">
              <p className="eyebrow">QUICK ACCESS</p>
              <h2>Workspace links</h2>
            </div>

            <div className="task-sidebar-links">
              <Link to="/tasks/user-tasks">My Tasks</Link>
              <Link to="/profile">Profile</Link>
              <Link to="/tasks/create-task">Create Task</Link>
            </div>
          </div>
        </aside>
      </section>
    </main>
  );
}

export default TaskDetailPage;
