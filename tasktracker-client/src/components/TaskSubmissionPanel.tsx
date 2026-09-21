import { useCallback, useEffect, useMemo, useState } from "react";
import { getTaskSubmissions, reviewSubmission, submitTask } from "../api/taskService";
import { errorMessage } from "../api/errorMessage";
import type { Task } from "../types/task";
import type { TaskSubmission } from "../types/taskSubmission";

export default function TaskSubmissionPanel({ task, onChanged }: {
  task: Task;
  onChanged: () => Promise<void>;
}) {
  const [submissions, setSubmissions] = useState<TaskSubmission[]>([]);
  const [content, setContent] = useState("");
  const [feedback, setFeedback] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState("");

  const load = useCallback(async () => {
    try {
      setSubmissions(await getTaskSubmissions(task.id));
      setFailure("");
    } catch (reason) {
      setFailure(errorMessage(reason, "Submission history could not be loaded."));
    } finally {
      setLoading(false);
    }
  }, [task.id]);

  useEffect(() => {
    const initial = window.setTimeout(() => { void load(); }, 0);
    return () => window.clearTimeout(initial);
  }, [load, task.version]);

  useEffect(() => {
    const refresh = () => { void Promise.allSettled([load(), onChanged()]); };
    window.addEventListener("focus", refresh);
    return () => window.removeEventListener("focus", refresh);
  }, [load, onChanged]);

  const latest = submissions.at(-1);
  const latestChanges = useMemo(() => [...submissions].reverse()
    .find((item) => item.review?.decision === "ChangesRequested"), [submissions]);

  const refresh = async () => {
    await Promise.allSettled([load(), onChanged()]);
  };

  const submit = async () => {
    if (busy || !content.trim()) return;
    setBusy(true); setFailure("");
    try {
      await submitTask(task.id, task.version, content);
      setContent("");
      await refresh();
    } catch (reason) {
      setFailure(errorMessage(reason, "Work could not be submitted. Your text has been kept."));
      await refresh();
    } finally { setBusy(false); }
  };

  const review = async (decision: "Approved" | "ChangesRequested") => {
    if (busy || !latest || (decision === "ChangesRequested" && !feedback.trim())) return;
    setBusy(true); setFailure("");
    try {
      await reviewSubmission(task.id, latest.id, task.version, decision, feedback);
      setFeedback("");
      await refresh();
    } catch (reason) {
      setFailure(errorMessage(reason, "The review could not be saved."));
      await refresh();
    } finally { setBusy(false); }
  };

  return <section className="task-detail-section submission-panel" aria-label="Work submission and review">
    <div className="task-section-header"><div><p className="eyebrow">DELIVERY</p><h2>Submission and review</h2></div></div>
    {failure && <p role="alert" className="error-message">{failure}</p>}
    {loading && <p>Loading submission history...</p>}

    {!loading && task.status === "InReview" && !latest && <div className="submission-notice submission-notice--warning">
      <strong>Legacy review state</strong>
      <p>This task has no valid submission to review. Cancel and reopen it to restart the delegated workflow.</p>
    </div>}

    {!loading && task.status === "InReview" && latest && <div className="submission-notice">
      <strong>Waiting for review · Revision {latest.revisionNumber}</strong>
      <p>Submitted by {latest.submittedByUserName} on {new Date(latest.createdAt).toLocaleString()}.</p>
      <div className="submission-content">{latest.content}</div>
    </div>}

    {task.canSubmit && <div className="submission-form">
      {latestChanges?.review?.feedback && <div className="submission-notice submission-notice--warning">
        <strong>Changes requested for revision {latestChanges.revisionNumber}</strong>
        <p>{latestChanges.review.feedback}</p>
      </div>}
      <label htmlFor="submission-content">Submit completed work</label>
      <textarea id="submission-content" rows={6} maxLength={10000} value={content} disabled={busy}
        onChange={(event) => setContent(event.target.value)} />
      <button type="button" className="primary-button" disabled={busy || !content.trim()}
        onClick={() => void submit()}>{busy ? "Submitting..." : "Submit work"}</button>
    </div>}

    {task.canReview && latest && <div className="submission-form">
      <label htmlFor="review-feedback">Feedback (required when requesting changes)</label>
      <textarea id="review-feedback" rows={4} maxLength={5000} value={feedback} disabled={busy}
        onChange={(event) => setFeedback(event.target.value)} />
      <div className="task-detail-actions">
        <button type="button" className="primary-button" disabled={busy}
          onClick={() => void review("Approved")}>Approve</button>
        <button type="button" className="secondary-button" disabled={busy || !feedback.trim()}
          onClick={() => void review("ChangesRequested")}>Request changes</button>
      </div>
    </div>}

    {!loading && submissions.length === 0 && task.status !== "InReview" &&
      <p>No work has been submitted yet.</p>}
    {submissions.length > 0 && <div className="submission-history">
      <h3>Revision history</h3>
      {[...submissions].reverse().map((item) => <article key={item.id} className="submission-revision">
        <header><strong>Revision {item.revisionNumber}</strong><span>{item.submittedByUserName} · {new Date(item.createdAt).toLocaleString()}</span></header>
        <div className="submission-content">{item.content}</div>
        {item.review && <div className={`submission-decision submission-decision--${item.review.decision.toLowerCase()}`}>
          <strong>{item.review.decision === "Approved" ? "Approved" : "Changes requested"}</strong>
          <span> by {item.review.reviewerUserName} · {new Date(item.review.createdAt).toLocaleString()}</span>
          {item.review.feedback && <p>{item.review.feedback}</p>}
        </div>}
      </article>)}
    </div>}
  </section>;
}
