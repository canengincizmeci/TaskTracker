import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import axiosClient from "../api/axiosClient";

import type { TaskPermission } from "../types/taskPermission";
import { errorMessage as getErrorMessage } from "../api/errorMessage";

function TaskSharePage() {
  const { taskId } = useParams();

  const [username, setUsername] = useState("");
  const [permission, setPermission] = useState<TaskPermission>("View");
  const [loading, setLoading] = useState(false);
  const [successMessage, setSuccessMessage] = useState("");
  const [errorMessage, setErrorMessage] = useState("");

  const handleShareTask = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    setSuccessMessage("");
    setErrorMessage("");

    if (!taskId) {
      setErrorMessage("Task id not found.");
      return;
    }

    if (!username.trim()) {
      setErrorMessage("Username is required.");
      return;
    }

    try {
      setLoading(true);

      await axiosClient.post("/TaskShare/invite-user", {
        taskRequestId: Number(taskId),
        username: username.trim(),
        permission,
      });

      setSuccessMessage("Invitation sent successfully.");
      setUsername("");
      setPermission("View");
    } catch (error: unknown) {
      setErrorMessage(getErrorMessage(error, "Could not send invitation."));
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="page public-page">
      <section className="task-detail-layout">
        <div className="task-detail-main">
          <div className="task-detail-header">
            <div>
              <p className="eyebrow">COLLABORATION</p>
              <h1>Share Task</h1>
            </div>

            <div className="task-detail-actions">
              <Link
                to={`/tasks/task-detail/${taskId}`}
                className="secondary-button"
              >
                Back to Task
              </Link>
            </div>
          </div>

          <p className="task-detail-description">
            Invite another user to this task and define what level of access
            they should have.
          </p>

          <section className="task-detail-section">
            <div className="task-section-header">
              <div>
                <p className="eyebrow">INVITE USER</p>
                <h2>Task sharing details</h2>
              </div>
            </div>

            <form onSubmit={handleShareTask} className="auth-form">
              <div className="form-group">
                <label htmlFor="username">Username</label>
                <input
                  id="username"
                  type="text"
                  placeholder="Enter username"
                  value={username}
                  onChange={(event) => setUsername(event.target.value)}
                />
              </div>

              <div className="form-group">
                <label htmlFor="permission">Permission</label>
                <select
                  id="permission"
                  value={permission}
                  onChange={(event) =>
                    setPermission(event.target.value as TaskPermission)
                  }
                >
                  <option value="View">View</option>
                  <option value="Edit">Edit</option>
                </select>
              </div>

              {successMessage && (
                <p className="success-message">{successMessage}</p>
              )}

              {errorMessage && <p className="error-message">{errorMessage}</p>}

              <button
                type="submit"
                className="primary-button"
                disabled={loading}
              >
                {loading ? "Sending..." : "Send Invitation"}
              </button>
            </form>
          </section>
        </div>

        <aside className="task-detail-sidebar">
          <div className="task-sidebar-card">
            <div className="task-sidebar-header">
              <p className="eyebrow">ACCESS CONTROL</p>
              <h2>Permission guide</h2>
            </div>

            <div className="task-sidebar-info">
              <span>View</span>
              <strong>Can view task</strong>
            </div>

            <div className="task-sidebar-info">
              <span>Edit</span>
              <strong>Can view and update task</strong>
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

export default TaskSharePage;
