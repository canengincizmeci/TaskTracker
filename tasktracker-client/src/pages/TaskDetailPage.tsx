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
                  <span>ME</span>
                </div>

                <div>
                  <strong>You</strong>
                  <span>{task.isOwner ? "Owner" : "Member"}</span>
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
                <strong>Task loaded</strong>
                <span>The task details were loaded successfully.</span>
              </div>

              <div className="timeline-item">
                <strong>Current status</strong>
                <span>Task status is {task.status}.</span>
              </div>

              <div className="timeline-item">
                <strong>Collaboration</strong>
                <span>Task sharing structure is ready for next updates.</span>
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
