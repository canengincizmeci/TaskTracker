import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import axiosClient from "../api/axiosClient";

type TaskPermission = 0 | 1;

function TaskSharePage() {
  const { taskId } = useParams();

  const [username, setUsername] = useState("");
  const [permission, setPermission] = useState<TaskPermission>(0);
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

      setSuccessMessage("Task shared successfully.");
      setUsername("");
      setPermission(0);
    } catch (error: any) {
      console.log("Invite User Error:", error.response?.data);

      const data = error.response?.data;

      const message =
        typeof data === "string"
          ? data
          : data?.message
          ? data.message
          : data?.title
          ? data.title
          : "An error occurred while sharing the task.";

      setErrorMessage(message);
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
                    setPermission(Number(event.target.value) as TaskPermission)
                  }
                >
                  <option value={0}>Read</option>
                  <option value={1}>Write</option>
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
                {loading ? "Sharing..." : "Share Task"}
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
              <span>Read</span>
              <strong>Can view task</strong>
            </div>

            <div className="task-sidebar-info">
              <span>Write</span>
              <strong>Can update task</strong>
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