import { useRef, useState, type ReactNode } from "react";
import type { Task } from "../types/task";
import type { TaskSubmission } from "../types/taskSubmission";
import type { TaskSubmissionWorkflow } from "../hooks/useTaskSubmissionWorkflow";
import { useTaskAttachments } from "../hooks/useTaskAttachments";
import { useTaskConversation } from "../hooks/useTaskConversation";
import { TaskDialog } from "./TaskEditDialog";
import { AttachmentChips, FilePicker } from "./TaskFileControls";
import "../styles/task-workflow.css";

const stageNames = { Pending: "Not started", InProgress: "In progress", InReview: "In review", Completed: "Completed" };
type Stage = keyof typeof stageNames;

function Progress({ task, delegated }: { task: Task; delegated: boolean }) {
  const steps: Stage[] = delegated ? ["Pending", "InProgress", "InReview", "Completed"] : ["Pending", "InProgress", "Completed"];
  const active = steps.findIndex(stage => stage === task.status);
  if (active < 0) return null;
  return <ol className="wf-progress" aria-label="Task progress">{steps.map((stage, index) => {
    const passed = index < active || task.status === "Completed";
    return <li key={stage} data-passed={passed} aria-current={index === active ? "step" : undefined}>
      <span className="wf-progress-mark" aria-hidden="true">{passed ? "✓" : ""}</span>
      <span>{stageNames[stage]}</span>
    </li>;
  })}</ol>;
}

function workHeading(task: Task, delegated: boolean, changes: boolean) {
  const actor = task.assigneeUserName || task.ownerUserName;
  if (task.status === "Completed") return ["Work complete", "The task is closed."];
  if (task.status === "Cancelled") return ["Work stopped", task.isOwner ? "Reopen this task from More task actions." : "The owner can reopen this task."];
  if (task.canStart) return ["Ready to start?", delegated ? `You’ll send your finished work to ${task.ownerUserName} for review.` :
    task.canManageResponsibility ? "Start the task yourself, or choose someone else from Assignee above." : "Start when you begin working, then mark the task complete when you’re done."];
  if (task.status === "Pending") return [`Waiting for ${actor}`, "Work begins when the responsible person starts this task."];
  if (changes && !task.canSubmit) return [`Waiting for a new revision from ${actor}`, "The latest feedback is below."];
  if (task.canSubmit) return [changes ? "Prepare your next revision" : "Prepare your delivery",
    changes ? "Read the latest feedback before sending your updated work." : `Send your finished work to ${task.ownerUserName} for review.`];
  if (task.status === "InProgress") {
    if (!delegated && task.isOwner) return ["Working on this task", "Mark it complete when the work is finished."];
    return [`${actor} is working`, delegated ? `${task.ownerUserName} will review the next delivery.` : `${actor} can mark this task complete.`];
  }
  if (task.canReview) return ["A delivery is ready for review", "Read the revision, then approve it or send feedback to the assignee."];
  if (task.status === "InReview") {
    if (task.isOwner) return ["Review unavailable", "The current assignment or submission no longer supports a review. You can cancel and reopen the task from More task actions."];
    return ["Waiting for owner review", `${task.ownerUserName} reviews the delivery and decides what happens next.`];
  }
  return ["Task details", ""];
}

function When({ value }: { value: string }) {
  return <time dateTime={value}>{new Date(value).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })}</time>;
}

function RevisionBody({ item, attachmentStatus = false }: { item: TaskSubmission; attachmentStatus?: boolean }) {
  const attachments = useTaskAttachments();
  return <div className="wf-revision-body">
    <p className="wf-summary">{item.content}</p>
    <AttachmentChips files={attachments.files.filter(file => file.kind === "Submission" && file.taskSubmissionId === item.id)} />
    {attachmentStatus && attachments.loading && <p role="status" className="wf-muted">Checking delivery files…</p>}
    {attachmentStatus && attachments.error && <p role="alert" className="wf-warning">
      Delivery files could not be loaded. <button type="button" className="tw-inline-action"
        onClick={() => void attachments.refresh().catch(() => undefined)}>Retry files</button></p>}
    {item.review && <div className={`wf-verdict${item.review.decision === "Approved" ? " wf-verdict--approved" : ""}`}>
      <strong>{item.review.decision === "Approved" ? "Approved" : "Changes requested"}</strong>
      <span>{item.review.reviewerUserName} · <When value={item.review.createdAt} /></span>
      {item.review.feedback && <p>{item.review.feedback}</p>}
    </div>}
  </div>;
}

function ReviewForm({ item, workflow, disabled }: { item: TaskSubmission; workflow: TaskSubmissionWorkflow; disabled: boolean }) {
  const feedbackInput = useRef<HTMLTextAreaElement>(null);
  const approveButton = useRef<HTMLButtonElement>(null);
  const [missingFeedback, setMissingFeedback] = useState(false);
  const [approval, setApproval] = useState<ReturnType<TaskSubmissionWorkflow["reviewSnapshot"]>>();
  return <div className="wf-form wf-review">
    <label htmlFor="review-feedback">Review feedback</label>
    <textarea ref={feedbackInput} id="review-feedback" rows={3} maxLength={5000} value={workflow.feedback}
      placeholder="What should the assignee know?" disabled={disabled} aria-invalid={missingFeedback || undefined}
      aria-describedby={missingFeedback ? "wf-feedback-error" : "wf-feedback-help"}
      onChange={event => { setMissingFeedback(false); workflow.setFeedback(event.target.value); }} />
    {missingFeedback ? <p id="wf-feedback-error" role="alert" className="wf-warning">Add feedback so the assignee knows what to change.</p>
      : <small id="wf-feedback-help">Requesting changes sends the task back to the assignee with your feedback.</small>}
    {workflow.reviewFileNotice && <p role="status" className="wf-warning">{workflow.reviewFileNotice}</p>}
    <div className="wf-buttons">
      <button ref={approveButton} type="button" className="tw-control tw-control--primary" disabled={disabled || !workflow.reviewFilesReady}
        onClick={() => setApproval(workflow.reviewSnapshot())}>{workflow.operation === "approve" ? "Approving…" : "Approve & complete"}</button>
      <button type="button" className="tw-control" disabled={disabled || !workflow.reviewFilesReady} onClick={() => {
        if (!workflow.feedback.trim()) { setMissingFeedback(true); feedbackInput.current?.focus(); return; }
        void workflow.review("ChangesRequested");
      }}>{workflow.operation === "requestChanges" ? "Sending…" : "Request changes"}</button>
    </div>
    {approval && <TaskDialog title={`Approve revision ${item.revisionNumber}?`} opener={approveButton} onClose={() => setApproval(undefined)}>
      <p>This approves the delivery from {item.submittedByUserName} and completes the task.</p>
      <p className="wf-muted">{approval.files.length === 0 ? "No delivery files are saved. You are approving the summary alone." :
        `${approval.files.length === 1 ? "1 delivery file" : `${approval.files.length} delivery files`} in the revision you reviewed.`}</p>
      <div className="tw-dialog-actions">
        <button type="button" className="tw-control" onClick={() => setApproval(undefined)}>Keep reviewing</button>
        <button type="button" className="tw-control tw-control--primary" disabled={disabled || !workflow.reviewFilesReady} onClick={() => {
          setApproval(undefined);
          void workflow.review("Approved", approval);
        }}>Confirm approval</button>
      </div>
    </TaskDialog>}
  </div>;
}

function UploadRecovery({ workflow, disabled }: { workflow: TaskSubmissionWorkflow; disabled: boolean }) {
  const pending = workflow.pendingUpload;
  if (!pending) return null;
  return <div className="wf-recovery">
    <strong role="status">Revision {pending.submission.revisionNumber} is saved. Its file upload still needs attention.</strong>
    <p>{pending.reason}</p>
    {workflow.canRetryAttachment ? <>
      <FilePicker label="Choose delivery file" file={pending.file} onChange={workflow.setRetryFile} disabled={disabled} />
      <div className="wf-buttons"><button type="button" className="tw-control" disabled={disabled || !pending.file}
        onClick={() => { if (pending.file) void workflow.attachToRevision(pending.submission.id, pending.file); }}>{workflow.operation === "attach" ? "Checking file…" : "Retry file upload"}</button>
        <button type="button" className="tw-inline-action" disabled={disabled} onClick={workflow.dismissUpload}>Dismiss</button></div>
    </> : workflow.loading ? <p role="status">Checking the saved revision…</p> : <>
      <p>{workflow.historyError || workflow.needsRefresh ? "Reload the workspace to check whether you can retry the file."
        : "This revision can no longer receive files."}</p>
      <button type="button" className="tw-inline-action" onClick={workflow.dismissUpload}>Dismiss</button>
    </>}
  </div>;
}

function RevisionFileForm({ id, workflow, disabled }: { id: number; workflow: TaskSubmissionWorkflow; disabled: boolean }) {
  const [selected, setSelected] = useState<File | null>(null);
  return <details className="wf-add-file"><summary>Attach a file to this revision</summary>
    <form className="wf-form" onSubmit={async e => {
      e.preventDefault();
      if (!selected || disabled) return;
      await workflow.attachToRevision(id, selected);
      setSelected(null);
    }}>
      <FilePicker label="Choose delivery file" file={selected} onChange={setSelected} disabled={disabled} />
      <button type="submit" className="tw-control" disabled={disabled || !selected}>
        {workflow.operation === "attach" ? "Attaching…" : "Attach file"}</button>
    </form>
  </details>;
}

function HistoryError({ workflow }: { workflow: TaskSubmissionWorkflow }) {
  return <p className="wf-warning" role="alert">{workflow.historyError}{" "}
    <button type="button" className="tw-inline-action" onClick={() => void workflow.reloadHistory()}>Retry submissions</button></p>;
}

export default function TaskSubmissionPanel({ task, workflow, onStart, onComplete, onOpenHistory, statusBusy, children }: {
  task: Task; workflow: TaskSubmissionWorkflow; onStart: () => void; onComplete: () => void;
  onOpenHistory?: () => void; statusBusy: boolean; children?: ReactNode;
}) {
  const artifactHeading = useRef<HTMLHeadingElement>(null);
  const composer = useRef<HTMLTextAreaElement>(null);
  const delegated = task.assigneeUserId != null && task.assigneeUserId !== task.ownerId;
  const latest = workflow.latest;
  const { liveError } = useTaskConversation();
  const changes = delegated && task.status === "InProgress" && task.workflowSubmissionId != null;
  const [title, detail] = workHeading(task, delegated, changes);
  const disabled = statusBusy || workflow.busy || workflow.needsRefresh;
  const showArtifact = latest && ((changes && latest.review?.decision === "ChangesRequested") || task.status === "InReview" ||
    (task.status === "Completed" && latest.review?.decision === "Approved"));
  const canCompose = task.canSubmit && !workflow.loading && !workflow.historyError;
  const showReview = task.canReview && showArtifact && !latest.review && latest.submittedByUserId === task.assigneeUserId;
  const jumpToDelivery = () => {
    const target = showArtifact ? artifactHeading.current : composer.current;
    target?.focus({ preventScroll: true });
    target?.scrollIntoView({ block: "center" });
  };
  return <>
    <section className="wf-next" aria-labelledby="wf-next-heading">
      <div className="wf-next-row"><div><h2 id="wf-next-heading" tabIndex={-1}>{title}</h2><p>{detail}</p></div>
        {task.canStart ? <button type="button" className="tw-control tw-control--primary" disabled={disabled}
          onClick={onStart}>{statusBusy ? "Starting…" : "Start task"}</button>
        : task.isOwner && !delegated && task.status === "InProgress" ? <button type="button" className="tw-control tw-control--primary"
          disabled={disabled} onClick={onComplete}>{statusBusy ? "Completing…" : "Complete task"}</button>
        : (showReview || canCompose) && <button type="button" className="tw-control tw-control--primary" onClick={jumpToDelivery}>
          {showReview ? `Read revision ${latest.revisionNumber}` : changes ? "Read feedback" : "Write delivery"}<span aria-hidden="true">↓</span>
        </button>}
      </div>
      <Progress task={task} delegated={delegated} />
    </section>
    {liveError && <div className="tw-notice" role="status">{liveError}{" "}
      <button type="button" className="tw-inline-action" onClick={() => void workflow.sync()}>Refresh task</button></div>}
    {children}
    {workflow.failure && <p role="alert" className="tw-notice tw-notice--error">{workflow.failure}</p>}
    {workflow.success && <p role="status" className="tw-notice">{workflow.success}</p>}
    {workflow.needsRefresh && <p role="alert" className="wf-warning">Check the latest task state before continuing.{" "}
      <button type="button" className="tw-inline-action" onClick={() => void workflow.sync()}>Reload workflow</button></p>}
    {(!showArtifact || workflow.pendingUpload?.submission.id !== latest.id) && <UploadRecovery workflow={workflow} disabled={disabled} />}
    {workflow.historyError && <HistoryError workflow={workflow} />}
    {workflow.loading && !showArtifact && (task.status === "InReview" || task.canSubmit) && <p role="status" className="wf-muted">Loading delivery…</p>}
    {!workflow.loading && !workflow.historyError && !latest && task.status === "InReview" && task.canViewParticipants &&
      <p className="wf-warning">No saved revision is available for this review.</p>}
    {showArtifact && <section className="wf-delivery" aria-labelledby="delivery-heading">
      <div className="wf-section-heading"><h2 ref={artifactHeading} tabIndex={-1} id="delivery-heading">Latest delivery</h2>
        {workflow.submissions.length > 1 && onOpenHistory && <button type="button" className="tw-inline-action" onClick={onOpenHistory}>Previous revisions</button>}</div>
      <article className="wf-paper"><header><h3>Revision {latest.revisionNumber}</h3>
        <p>{latest.submittedByUserName} · <When value={latest.createdAt} /></p></header>
        {task.status === "InReview" && !latest.review && <p className="wf-muted">Awaiting owner review</p>}
        <RevisionBody item={latest} attachmentStatus />
        {workflow.pendingUpload?.submission.id === latest.id ? <UploadRecovery workflow={workflow} disabled={disabled} /> :
          workflow.canAttach && <RevisionFileForm key={latest.id} id={latest.id} workflow={workflow} disabled={disabled} />}
        {showReview && <ReviewForm key={latest.id} item={latest} workflow={workflow} disabled={disabled} />}
      </article>
    </section>}
    {canCompose && <form className="wf-delivery wf-form" onSubmit={event => { event.preventDefault(); void workflow.submit(); }}>
      <h2>{changes ? "New revision" : "Delivery"}</h2>
      <label htmlFor="submission-content">Work summary</label>
      <textarea ref={composer} id="submission-content" rows={4} maxLength={10000} disabled={disabled} value={workflow.content}
        placeholder={changes ? "Describe the updates you made in response to the feedback…" : "Describe what you completed and where to find the result…"}
        onChange={event => workflow.setContent(event.target.value)} />
      <div className="wf-compose-actions">
        <FilePicker label="Attach delivery file" file={workflow.file} onChange={workflow.setFile} disabled={disabled} />
        <button type="submit" className="tw-control tw-control--primary" disabled={disabled || !workflow.content.trim()}>
          {workflow.operation === "submit" ? "Submitting…" : changes ? "Resubmit for review" : "Submit for review"}</button>
      </div>
    </form>}
  </>;
}

export function TaskSubmissionHistory({ task, workflow, onOpenCurrent }: {
  task: Task; workflow: TaskSubmissionWorkflow; onOpenCurrent: () => void;
}) {
  if (!task.canViewParticipants) return null;
  if (!workflow.loading && !workflow.historyError && workflow.submissions.length === 0 && !task.assigneeUserId) return null;
  return <section className="wf-history" aria-labelledby="wf-history-heading">
    <h2 id="wf-history-heading">Submission history</h2>
    {task.status === "InReview" && task.workflowSubmissionId != null && <p className="wf-muted">
      {workflow.latest ? `Revision ${workflow.latest.revisionNumber} is awaiting review.` : "A delivery is awaiting review."}{" "}
      <button type="button" className="tw-inline-action" onClick={onOpenCurrent}>Open current delivery in Work</button></p>}
    {workflow.loading && <p role="status" className="wf-muted">{workflow.submissions.length ? "Refreshing revisions…" : "Loading revisions…"}</p>}
    {workflow.historyError && <HistoryError workflow={workflow} />}
    {!workflow.loading && !workflow.historyError && workflow.submissions.length === 0 && <p className="wf-muted">No revisions have been submitted.</p>}
    {workflow.submissions.slice().reverse().map(item => <details className="wf-history-entry" key={item.id}>
        <summary><strong>Revision {item.revisionNumber}</strong><span>{item.submittedByUserName} · <When value={item.createdAt} /></span>
          <small>{item.review?.decision === "Approved" ? "Approved" : item.review ? "Changes requested" : "Submitted"}</small></summary>
        <RevisionBody item={item} attachmentStatus />
      </details>)}
  </section>;
}
