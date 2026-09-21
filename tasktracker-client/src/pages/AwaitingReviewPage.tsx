import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getAwaitingReview } from "../api/taskService";
import { errorMessage } from "../api/errorMessage";
import type { AwaitingReviewTask } from "../types/taskSubmission";
import LoadingSpinner from "../components/LoadingSpinner";

export default function AwaitingReviewPage() {
  const [tasks, setTasks] = useState<AwaitingReviewTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [failure, setFailure] = useState("");

  const load = useCallback(async () => {
    try { setTasks(await getAwaitingReview()); setFailure(""); }
    catch (reason) { setFailure(errorMessage(reason, "Awaiting review could not be loaded.")); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => {
    const initial = window.setTimeout(() => { void load(); }, 0);
    const refresh = () => { void load(); };
    window.addEventListener("focus", refresh);
    return () => { window.clearTimeout(initial); window.removeEventListener("focus", refresh); };
  }, [load]);

  if (loading) return <main className="page public-page"><LoadingSpinner text="Loading review queue..." /></main>;
  return <main className="page public-page"><section className="utasks-content-card">
    <div className="utasks-section-header"><div><p className="eyebrow">OWNER REVIEW</p><h1>Awaiting Review</h1></div>
      <button type="button" className="secondary-button" onClick={() => void load()}>Refresh</button></div>
    <p>Delegated work waiting for your decision, oldest submission first.</p>
    {failure && <p role="alert" className="error-message">{failure}</p>}
    {!failure && tasks.length === 0 && <div className="utasks-empty"><h2>Nothing waiting</h2>
      <p>Submitted work will appear here even if a notification is missed.</p></div>}
    <section className="utasks-grid">{tasks.map((task) => <article className="utasks-card" key={task.taskId}>
      <div className="utasks-card__top"><span className="utasks-pill">{task.latestRevisionNumber ? `Revision ${task.latestRevisionNumber}` : "Legacy state"}</span>
        <span>{task.submittedAt ? new Date(task.submittedAt).toLocaleString() : "No valid submission"}</span></div>
      <h2>{task.title}</h2><p>Assignee: {task.assigneeUserName ?? "Unassigned"}</p>
      <div className="utasks-card__meta"><span>{task.dueDate ? `Due ${new Date(task.dueDate).toLocaleDateString()}` : "No due date"}</span></div>
      <div className="utasks-card__footer"><Link className="primary-button" to={`/tasks/task-detail/${task.taskId}`}>Open task</Link></div>
    </article>)}</section>
  </section></main>;
}
