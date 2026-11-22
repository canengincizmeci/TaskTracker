import { useId, useLayoutEffect, useRef, useState, type MouseEvent } from "react";
import { assignSubTask, deleteSubTask, downloadSubTaskFile, reviewSubTask, submitSubTask, subTaskAction,
  updateSubTask, uploadSubTaskFile } from "../api/subTaskService";
import { subTaskMutationError, subTaskResponseStatus, useSubTaskDetails } from "../hooks/useSubTasks";
import type { SubTask, SubTaskFields } from "../types/subTask";
import type { Task } from "../types/task";
import type { TaskAttachment } from "../types/taskAttachment";
import { taskStatusLabel } from "../utils/taskDisplay";
import { SubTaskAssigneeDialog, SubTaskConfirmation, SubTaskFieldsDialog, type SubTaskDialogState } from "./SubTaskDialogs";
import SubTaskHistory, { SubTaskRevision } from "./SubTaskHistory";
import { FilePicker } from "./TaskFileControls";

const dialogCapability = {
  edit: "canEdit", assign: "canAssign", delete: "canDelete", cancel: "canCancel", approve: "canReview",
} as const;
type Dialog = keyof typeof dialogCapability;

export function SubTaskMetadata({ item }: { item: SubTask }) {
  return <div className="st-meta">
    <span className={`tw-status tw-status--${item.status.toLowerCase()}`}>{taskStatusLabel(item.status)}</span>
    <span>{item.assigneeUserName || (item.assigneeUserId == null ? "Unassigned" : "Assigned")}</span>
    <span>{item.priority} priority</span>
    {item.dueDate ? <time dateTime={item.dueDate}>Due {new Date(item.dueDate + "T00:00:00").toLocaleDateString(undefined,
      { dateStyle: "medium" })}</time> : <span>No due date</span>}
  </div>;
}

export default function SubTaskDetail({ task, id, panelId, refreshKey, rosterRevision, parentLocked, active, disabled: parentBusy,
  draft, setDraft, feedbackFor, saveFeedback, reload, refreshParent, onClose, onDeleted }: {
  task: Task; id: number; panelId: string; refreshKey: string; rosterRevision: number; parentLocked: boolean; disabled: boolean;
  active: boolean; draft: string; setDraft: (value: string) => void;
  feedbackFor: (submissionId: number) => string; saveFeedback: (submissionId: number, value: string) => void;
  reload: () => void; refreshParent: () => Promise<unknown>;
  onClose: () => void; onDeleted: () => void;
}) {
  const details = useSubTaskDetails(task.id, id, refreshKey, reload);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [dialog, setDialog] = useState<{ mode: Dialog; item: SubTask; invalidated?: boolean }>();
  const [fileDraft, setFileDraft] = useState<{ submissionId: number; file: File }>();
  const doing = useRef(false);
  const focused = useRef(false);
  const opener = useRef<HTMLElement>(null);
  const article = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const label = useId();
  const item = details.data?.item;
  const capabilities = parentLocked ? null : item?.capabilities;
  const disabled = !active || parentBusy || busy || details.loading || Boolean(details.error);
  const current = details.data?.submissions.find(submission => submission.id === item?.currentSubmissionId && !submission.review);
  const latest = details.data?.submissions[0];
  const visibleRevision = current ?? (latest?.review ? latest : undefined);
  const message = current ? feedbackFor(current.id) : "";
  const files = details.data?.files;
  const evidenceCurrent = files?.version === item?.version;
  const file = fileDraft && fileDraft.submissionId === current?.id ? fileDraft.file : null;
  const download = (attachment: TaskAttachment) => item ? downloadSubTaskFile(item, attachment) : Promise.resolve();

  useLayoutEffect(() => {
    if (active && item && !focused.current) {
      heading.current?.focus();
      focused.current = true;
    }
  }, [active, item]);

  function openDialog(mode: Dialog, event: MouseEvent<HTMLButtonElement>) {
    if (!item || disabled || !capabilities?.[dialogCapability[mode]]) return;
    opener.current = event.currentTarget;
    setError(""); setNotice(""); setDialog({ mode, item });
  }

  async function perform(action: () => Promise<void>, success: string, after?: () => void) {
    if (disabled || doing.current) return;
    doing.current = true; setBusy(true); setError(""); setNotice("");
    try {
      await action();
      setDialog(undefined);
      setNotice(success);
      after?.();
      reload();
      if (!dialog && article.current?.contains(document.activeElement)) heading.current?.focus({ preventScroll: true });
    } catch (failure) {
      const status = subTaskResponseStatus(failure);
      setError(subTaskMutationError(failure));
      if (status === 409 || status === 403 || status === 404 || status == null || status >= 500) {
        setDialog(previous => previous && { ...previous, invalidated: true });
        reload();
        details.reload();
        void refreshParent().catch(() => undefined);
      }
    } finally {
      doing.current = false; setBusy(false);
    }
  }

  const staleDialog = dialog && (dialog.invalidated || !item || item.version !== dialog.item.version ||
    item.currentSubmissionId !== dialog.item.currentSubmissionId);
  const dialogState: SubTaskDialogState | undefined = (active && dialog) ? {
    busy, blocked: disabled || Boolean(staleDialog) || !capabilities?.[dialogCapability[dialog.mode]] ||
      (dialog.mode === "approve" && (!current || !evidenceCurrent)),
    error: error || (staleDialog ? "The subtask changed. Close and reopen this dialog to use its latest state." :
      !details.loading && !capabilities?.[dialogCapability[dialog.mode]] ? "This action is no longer available." : ""),
    onClose: () => { setDialog(undefined); setError(""); },
    opener, fallback: heading,
  } : undefined;
  function saveFields(fields: SubTaskFields) {
    if (dialog && !dialogState?.blocked) void perform(() => updateSubTask(dialog.item, fields), "Subtask updated.");
  }

  return <article ref={article} id={panelId} className="st-detail" aria-labelledby={label}>
    <div className="st-heading">
      <h3 ref={heading} id={label} tabIndex={-1}>{item?.title ?? "Subtask details"}</h3>
      <button type="button" className="tw-control" onClick={onClose}>Close details</button>
    </div>
    {notice && <p className="tw-notice" role="status">{notice}</p>}
    {error && !dialog && <p className="tw-notice tw-notice--error" role="alert">{error}</p>}
    {details.loading && <p className="st-muted" role="status">{item ? "Refreshing subtask…" : "Loading subtask…"}</p>}
    {details.error && <p className="tw-notice tw-notice--error" role="alert">{details.error}
      <button type="button" className="tw-inline-action" onClick={() => { reload(); details.reload(); }}>Reload subtask</button></p>}
    {item && details.data && <>
      <SubTaskMetadata item={item} />
      <p className="st-description">{item.description}</p>
      {capabilities && <div className="st-actions">
        {capabilities.canStart && <button type="button" className="tw-control tw-control--primary" disabled={disabled}
          onClick={() => void perform(() => subTaskAction(item, "start"), "Subtask started.")}>Start subtask</button>}
        {capabilities.canComplete && <button type="button" className="tw-control tw-control--primary" disabled={disabled}
          onClick={() => void perform(() => subTaskAction(item, "complete"), "Subtask completed.")}>Complete subtask</button>}
        {capabilities.canReopen && <button type="button" className="tw-control tw-control--primary" disabled={disabled}
          onClick={() => void perform(() => subTaskAction(item, "reopen"), "Subtask reopened.")}>Reopen subtask</button>}
        {capabilities.canEdit && <button type="button" className="tw-control" disabled={disabled}
          onClick={event => openDialog("edit", event)}>Edit subtask</button>}
        {capabilities.canAssign && <button type="button" className="tw-control" disabled={disabled}
          onClick={event => openDialog("assign", event)}>{item.assigneeUserId == null ? "Assign subtask" : "Change assignee"}</button>}
        {capabilities.canCancel && <button type="button" className="tw-control" disabled={disabled}
          onClick={event => openDialog("cancel", event)}>Cancel subtask</button>}
        {capabilities.canDelete && <button type="button" className="tw-control" disabled={disabled}
          onClick={event => openDialog("delete", event)}>Delete subtask</button>}
      </div>}
      {capabilities?.canSubmit && <form className="wf-form st-delivery" onSubmit={event => {
        event.preventDefault();
        if (draft.trim()) void perform(() => submitSubTask(item, draft.trim()), "Subtask work submitted.", () => setDraft(""));
      }}>
        <label>Subtask work summary<textarea rows={4} maxLength={10000} disabled={disabled} required
          value={draft} onChange={event => setDraft(event.target.value)} /></label>
        <div className="st-actions"><button type="submit" className="tw-control tw-control--primary"
          disabled={disabled || !draft.trim()}>Submit subtask for review</button></div>
      </form>}
      {draft && !capabilities?.canSubmit && <details className="st-draft">
        <summary>Unsent subtask draft</summary><p className="st-description">{draft}</p>
      </details>}
      {capabilities?.canReview && !current && !details.loading && <p className="tw-notice" role="status">
        The current revision could not be confirmed.
        <button type="button" className="tw-inline-action" onClick={() => { reload(); details.reload(); }}>Reload subtask</button></p>}
      {visibleRevision && <section className="st-delivery" aria-label="Latest subtask submission">
        <h4>{current ? capabilities?.canReview ? "Review revision" : "Awaiting review · Revision" :
          visibleRevision.review?.decision === "Approved" ? "Approved revision" : "Changes requested · Revision"} {visibleRevision.revisionNumber}</h4>
        <SubTaskRevision item={visibleRevision} files={files?.files ?? []} download={download} />
        {current && !files?.uploadsEnabled && <p className="st-muted">Subtask delivery file uploads are unavailable.</p>}
        {!parentLocked && current && files?.uploadSubmissionId === current.id && <form className="wf-form" onSubmit={event => {
          event.preventDefault();
          if (file && evidenceCurrent) void perform(() => uploadSubTaskFile(item, current.id, file),
            "Delivery file saved.", () => setFileDraft(undefined));
        }}>
          <FilePicker file={file} label="Choose subtask delivery file" disabled={disabled || !evidenceCurrent}
            onChange={next => setFileDraft(next ? { submissionId: current.id, file: next } : undefined)} />
          <div className="st-actions"><button type="submit" className="tw-control" disabled={disabled || !evidenceCurrent || !file}>
            Upload delivery file</button></div>
        </form>}
        {capabilities?.canReview && !evidenceCurrent && !details.loading && <p className="tw-notice" role="status">
          The delivery files changed. <button type="button" className="tw-inline-action" onClick={() => { reload(); details.reload(); }}>Reload revision</button></p>}
        {capabilities?.canReview && current && <form className="wf-form" onSubmit={event => {
          event.preventDefault();
          if (message.trim() && evidenceCurrent) void perform(() => reviewSubTask(item, "ChangesRequested", message),
            "Changes requested.", () => saveFeedback(current.id, ""));
        }}>
          <label>Subtask review feedback<textarea rows={3} maxLength={5000} disabled={disabled}
            value={message} onChange={event => saveFeedback(current.id, event.target.value)} /></label>
          <div className="st-actions">
            <button type="button" className="tw-control tw-control--primary" disabled={disabled || !evidenceCurrent}
              onClick={event => openDialog("approve", event)}>Approve subtask</button>
            <button type="submit" className="tw-control" disabled={disabled || !evidenceCurrent || !message.trim()}>Request changes</button>
          </div>
        </form>}
      </section>}
      <SubTaskHistory submissions={details.data.submissions} activities={details.data.activities}
        files={files?.files ?? []} download={download} />
    </>}
    {dialog?.mode === "edit" && dialogState && <SubTaskFieldsDialog item={dialog.item} state={dialogState} onSave={saveFields} />}
    {dialog?.mode === "assign" && dialogState && <SubTaskAssigneeDialog task={task} item={dialog.item} state={dialogState}
      revision={`${refreshKey}:${rosterRevision}`} onSave={userId => {
        if (!dialogState.blocked) void perform(() => userId == null ? subTaskAction(dialog.item, "unassign") :
          assignSubTask(dialog.item, userId), userId == null ? "Subtask unassigned." : "Subtask assigned.");
      }} />}
    {dialog?.mode === "cancel" && dialogState && <SubTaskConfirmation state={dialogState}
      title="Cancel this subtask?" explanation="Work will stop. You can reopen this subtask later."
      label="Cancel subtask" danger onConfirm={() => void perform(() => subTaskAction(dialog.item, "cancel"), "Subtask cancelled.")} />}
    {dialog?.mode === "delete" && dialogState && <SubTaskConfirmation state={dialogState}
      title="Delete this subtask?" explanation="This subtask and its history will be hidden from the task workspace."
      label="Delete subtask" danger onConfirm={() => void perform(() => deleteSubTask(dialog.item), "Subtask deleted.", () => {
        if (article.current?.isConnected) onDeleted();
      })} />}
    {dialog?.mode === "approve" && dialogState && <SubTaskConfirmation state={dialogState}
      title="Approve this subtask submission?" explanation="This completes the subtask. The parent task keeps its current status."
      label="Confirm approval" onConfirm={() => void perform(() => reviewSubTask(dialog.item, "Approved", message),
        "Subtask approved and completed.", () => {
          if (dialog.item.currentSubmissionId != null) saveFeedback(dialog.item.currentSubmissionId, "");
        })} />}
  </article>;
}
