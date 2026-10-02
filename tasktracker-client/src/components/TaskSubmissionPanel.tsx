import type { Task } from "../types/task";
import type { TaskSubmissionWorkflow } from "../hooks/useTaskSubmissionWorkflow";

export default function TaskSubmissionPanel({ task, workflow, onStart, onComplete, statusBusy }: {
  task: Task;
  workflow: TaskSubmissionWorkflow;
  onStart: () => void;
  onComplete: () => void;
  statusBusy: boolean;
}) {
  const selfManaged = !task.assigneeUserId || task.assigneeUserId === task.ownerId;
  const changesRequested = workflow.latest?.review?.decision === "ChangesRequested";

  let title = "No action needed";
  let message = "You can review the task details and discussion below.";
  if (task.canReview) title = "Review submitted work";
  else if (task.canSubmit) title = workflow.loading ? "Loading next action..." :
    changesRequested ? "Revise & resubmit" : "Submit work";
  else if (task.canStart) title = "Start task";
  else if (task.isOwner && selfManaged && task.status === "InProgress") title = "Complete task";
  else if (task.isOwner && !selfManaged && task.status === "Pending") {
    title = "Waiting for assignee to start";
    message = `${task.assigneeUserName ?? "The assignee"} can start this task when ready.`;
  } else if (task.isOwner && !selfManaged && task.status === "InProgress") {
    title = "Waiting for assignee to submit";
    message = `${task.assigneeUserName ?? "The assignee"} is working on this task.`;
  } else if (task.isAssignee && task.status === "InReview") {
    title = "Waiting for review";
    message = "The task owner will review your submitted work.";
  } else if (task.status === "InReview") {
    title = "Review unavailable";
    message = "The submitted work cannot be reviewed in its current state.";
  } else if (task.status === "Completed") {
    title = "Task completed";
    message = "No further workflow action is required.";
  } else if (task.status === "Cancelled") {
    title = "Task cancelled";
    message = "The owner can reopen this task from More actions.";
  }

  const hasPrimaryAction = task.canStart || task.canSubmit || task.canReview ||
    Boolean(task.isOwner && selfManaged && task.status === "InProgress");

  return <section className="task-next-action" aria-labelledby="task-next-action-title">
    <div className="task-next-action__heading"><p className="eyebrow">NEXT ACTION</p>
      <h2 id="task-next-action-title">{title}</h2></div>
    {workflow.failure && <p role="alert" className="error-message">{workflow.failure}</p>}
    {workflow.loading && (task.canSubmit || task.canReview || task.status === "InReview") &&
      <p role="status">Loading submitted work...</p>}

    {!workflow.loading && !workflow.historyLoadFailed && task.status === "InReview" && !workflow.latest && <div className="submission-notice submission-notice--warning">
      <strong>Submitted work is unavailable</strong>
      <p>This task is waiting for review but has no valid submitted work. The owner can cancel and reopen it from More actions.</p>
    </div>}

    {task.canReview && workflow.latest && <div className="submission-form">
      <p>Submitted by <strong>{workflow.latest.submittedByUserName}</strong> on {new Date(workflow.latest.createdAt).toLocaleString()}.</p>
      <div className="submission-content">{workflow.latest.content}</div>
      <label htmlFor="review-feedback">Feedback (required when requesting changes)</label>
      <textarea id="review-feedback" rows={4} maxLength={5000} value={workflow.feedback}
        disabled={workflow.busy} onChange={(event) => workflow.setFeedback(event.target.value)} />
      <div className="task-next-action__buttons">
        <button type="button" className="primary-button" disabled={workflow.busy}
          onClick={() => void workflow.review("Approved")}>Approve</button>
        <button type="button" className="secondary-button" disabled={workflow.busy || !workflow.feedback.trim()}
          onClick={() => void workflow.review("ChangesRequested")}>Request changes</button>
      </div>
    </div>}

    {task.canSubmit && !workflow.loading && <div className="submission-form">
      {changesRequested && workflow.latest?.review?.feedback && <div className="submission-notice submission-notice--warning">
        <strong>Changes requested</strong><p>{workflow.latest.review.feedback}</p>
      </div>}
      <label htmlFor="submission-content">{changesRequested ? "Updated work" : "Completed work"}</label>
      <textarea id="submission-content" rows={6} maxLength={10000} value={workflow.content}
        disabled={workflow.busy} onChange={(event) => workflow.setContent(event.target.value)} />
      <button type="button" className="primary-button" disabled={workflow.busy || !workflow.content.trim()}
        onClick={() => void workflow.submit()}>{workflow.busy ? "Submitting..." : changesRequested ? "Resubmit work" : "Submit work"}</button>
    </div>}

    {task.canStart && <button type="button" className="primary-button" disabled={statusBusy}
      onClick={onStart}>{statusBusy ? "Starting..." : "Start task"}</button>}
    {!task.canStart && !task.canSubmit && !task.canReview && task.isOwner && selfManaged && task.status === "InProgress" &&
      <button type="button" className="primary-button" disabled={statusBusy}
        onClick={onComplete}>{statusBusy ? "Completing..." : "Complete task"}</button>}
    {!hasPrimaryAction && task.status !== "InReview" && <p className="task-next-action__message">{message}</p>}
    {!hasPrimaryAction && task.status === "InReview" && workflow.latest && <p className="task-next-action__message">{message}</p>}
  </section>;
}

export function TaskSubmissionHistory({ task, workflow }: { task: Task; workflow: TaskSubmissionWorkflow }) {
  const delegated = task.assigneeUserId != null && task.assigneeUserId !== task.ownerId;
  if (!delegated && task.status !== "InReview" && workflow.submissions.length === 0) return null;
  return <section className="task-history-section" aria-labelledby="submitted-work-history-title">
    <h3 id="submitted-work-history-title">Submitted work</h3>
    {workflow.loading && <p>Loading submitted work history...</p>}
    {!workflow.loading && workflow.submissions.length === 0 && <p>No submitted work history is available.</p>}
    {workflow.submissions.length > 0 && <div className="submission-history">
      {[...workflow.submissions].reverse().map((item) => <article key={item.id} className="submission-revision">
        <header><strong>Submission {item.revisionNumber}</strong><span>{item.submittedByUserName} · {new Date(item.createdAt).toLocaleString()}</span></header>
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
