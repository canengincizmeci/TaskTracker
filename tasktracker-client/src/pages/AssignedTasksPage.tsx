import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getAssignedTasks } from "../api/taskService";
import { errorMessage } from "../api/errorMessage";
import type { Task } from "../types/task";
import LoadingSpinner from "../components/LoadingSpinner";

export default function AssignedTasksPage() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);
  const [failure, setFailure] = useState("");
  useEffect(() => {
    let active = true;
    getAssignedTasks().then((items) => { if (active) setTasks(items); })
      .catch((reason: unknown) => { if (active) setFailure(errorMessage(reason, "Could not load assigned work.")); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);
  if (loading) return <main className="page public-page"><LoadingSpinner text="Loading assigned work..." /></main>;
  return <main className="page public-page"><section className="utasks-content-card">
    <div className="utasks-section-header"><div><p className="eyebrow">RESPONSIBILITY</p><h1>Assigned to me</h1></div>
      <Link className="secondary-button" to="/tasks/user-tasks">Owned tasks</Link></div>
    <p>Work where you are the current assignee.</p>
    {failure && <p role="alert" className="error-message">{failure}</p>}
    {!failure && tasks.length === 0 && <div className="utasks-empty"><h2>No assigned work</h2>
      <p>Tasks will appear here when an owner assigns them to you.</p></div>}
    <section className="utasks-grid">{tasks.map((task) => <article className="utasks-card" key={task.id}>
      <div className="utasks-card__top"><span className="utasks-pill">{task.priority}</span><span>{task.status}</span></div>
      <h2>{task.title}</h2><p>Owner: {task.ownerUserName}</p>
      <div className="utasks-card__meta"><span>{task.category}</span>
        <span>{task.dueDate ? `Due ${new Date(task.dueDate).toLocaleDateString()}` : "No due date"}</span></div>
      <Link className="primary-button" to={`/tasks/task-detail/${task.id}`}>Open task</Link>
    </article>)}</section>
  </section></main>;
}
