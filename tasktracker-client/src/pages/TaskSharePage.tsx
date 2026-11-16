import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import axiosClient from "../api/axiosClient";
import { errorMessage as getErrorMessage } from "../api/errorMessage";
import { getTaskById } from "../api/taskService";
import type { Task } from "../types/task";
import type { TaskPermission } from "../types/taskPermission";

function TaskSharePage() {
  const { taskId } = useParams();
  const numericTaskId = Number(taskId);
  const validTaskId = Number.isInteger(numericTaskId) && numericTaskId > 0;

  const [task, setTask] = useState<Task | null>(null);
  const [contextLoading, setContextLoading] = useState(validTaskId);
  const [contextError, setContextError] = useState(validTaskId ? "" : "This task address is invalid.");
  const [username, setUsername] = useState("");
  const [permission, setPermission] = useState<TaskPermission>("View");
  const [loading, setLoading] = useState(false);
  const [successMessage, setSuccessMessage] = useState("");
  const [errorMessage, setErrorMessage] = useState("");

  useEffect(() => {
    if (!validTaskId) return;
    let cancelled = false;
    getTaskById(numericTaskId)
      .then((data) => {
        if (!cancelled) setTask(data);
      })
      .catch((error: unknown) => {
        if (!cancelled) setContextError(getErrorMessage(error, "Could not load this task."));
      })
      .finally(() => {
        if (!cancelled) setContextLoading(false);
      });
    return () => { cancelled = true; };
  }, [numericTaskId, validTaskId]);

  const handleShareTask = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSuccessMessage("");
    setErrorMessage("");

    if (!validTaskId) {
      setErrorMessage("Task id not found.");
      return;
    }
    if (!username.trim()) {
      setErrorMessage("Enter the username of the person you want to invite.");
      return;
    }

    try {
      setLoading(true);
      await axiosClient.post("/TaskShare/invite-user", {
        taskRequestId: numericTaskId,
        username: username.trim(),
        permission,
      });
      setSuccessMessage(`Invitation sent to ${username.trim()}.`);
      setUsername("");
      setPermission("View");
    } catch (error: unknown) {
      setErrorMessage(getErrorMessage(error, "Could not send invitation."));
    } finally {
      setLoading(false);
    }
  };

  const canShare = task?.canShare !== false;
  const backPath = validTaskId ? `/tasks/task-detail/${numericTaskId}` : "/dashboard";

  return (
    <main className="page public-page task-share-page">
      <div className="task-share-shell">
        <Link to={backPath} className="task-share-back">← {validTaskId ? "Back to task" : "Back to dashboard"}</Link>

        <header className="task-share-header">
          <div>
            <p className="eyebrow">TASK ACCESS</p>
            <h1>Share a task</h1>
            <p>Invite one TaskTracker user and choose the access they need.</p>
          </div>
          {task && <span className="task-share-context-label">Task #{task.id}</span>}
        </header>

        {contextLoading && <div className="task-share-state" role="status">Loading task context...</div>}
        {contextError && <div className="task-share-state task-share-state--error" role="alert">
          <strong>Task unavailable</strong><span>{contextError}</span>
        </div>}

        {task && <div className="task-share-layout">
          <section className="task-share-panel" aria-labelledby="share-task-title">
            <div className="task-share-task-context">
              <span>Sharing access to</span>
              <h2 id="share-task-title">{task.title}</h2>
              <p>{task.category} · Owned by {task.ownerUserName}</p>
            </div>

            {!canShare ? <div className="task-share-state task-share-state--error" role="alert">
              <strong>Sharing is not available</strong>
              <span>Only the task owner can invite collaborators to this task.</span>
            </div> : <form onSubmit={handleShareTask} className="task-share-form" noValidate>
              <div className="form-group">
                <label htmlFor="share-username">Username</label>
                <input id="share-username" type="text" autoComplete="off" placeholder="e.g. alex"
                  value={username} onChange={(event) => setUsername(event.target.value)} disabled={loading}
                  maxLength={50} aria-describedby="share-username-hint" required />
                <small id="share-username-hint">Enter the exact TaskTracker username.</small>
              </div>

              <fieldset className="task-share-permissions">
                <legend>Permission</legend>
                <label className={permission === "View" ? "task-share-permission is-selected" : "task-share-permission"}>
                  <input type="radio" name="permission" value="View" checked={permission === "View"}
                    onChange={() => setPermission("View")} disabled={loading} />
                  <span><strong>View</strong><small>Can open the task and read its current details.</small></span>
                </label>
                <label className={permission === "Edit" ? "task-share-permission is-selected" : "task-share-permission"}>
                  <input type="radio" name="permission" value="Edit" checked={permission === "Edit"}
                    onChange={() => setPermission("Edit")} disabled={loading} />
                  <span><strong>Edit</strong><small>Can view and update task details permitted by the existing workflow.</small></span>
                </label>
              </fieldset>

              {successMessage && <p className="task-share-notice task-share-notice--success" role="status">{successMessage}</p>}
              {errorMessage && <p className="task-share-notice task-share-notice--error" role="alert">{errorMessage}</p>}

              <div className="task-share-actions">
                <button type="submit" className="primary-button" disabled={loading}>
                  {loading ? "Sending invitation..." : "Send invitation"}
                </button>
                <Link to={backPath} className="secondary-button">Cancel</Link>
              </div>
            </form>}
          </section>

          <aside className="task-share-guide" aria-labelledby="permission-guide-title">
            <p className="eyebrow">BEFORE YOU SEND</p>
            <h2 id="permission-guide-title">Access stays controlled</h2>
            <ul>
              <li>The invitation applies only to this task.</li>
              <li>View access does not permit task updates.</li>
              <li>Edit access follows the task’s existing lifecycle and authorization rules.</li>
            </ul>
          </aside>
        </div>}
      </div>
    </main>
  );
}

export default TaskSharePage;
