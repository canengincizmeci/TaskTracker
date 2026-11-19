import { useRef, useState } from "react";
import { isAxiosError } from "axios";
import { uploadTaskAttachment } from "../api/taskAttachmentService";
import { errorMessage } from "../api/errorMessage";
import { useTaskAttachments } from "../hooks/useTaskAttachments";
import { useTaskConversation } from "../hooks/useTaskConversation";
import type { TaskSubmission } from "../types/taskSubmission";
import { AttachmentChips, CollaborationIcon, FilePicker } from "./TaskFileControls";

export function TaskReferenceFiles({ onOpenFiles }: { onOpenFiles: () => void }) {
  const { files, loading, error } = useTaskAttachments();
  const references = files.filter((item) => item.kind === "Task");
  return <section className="tc-references" aria-label="Task reference files">
    <div className="tc-section-head"><h3>Reference files</h3>
      <button className="tc-link" type="button" onClick={onOpenFiles}>
        {references.length || error ? "Open files" : "Add a file"} <span aria-hidden="true">↗</span>
      </button></div>
    {error ? <p className="tc-muted">Reference files could not be loaded.</p> : references.length ?
      <AttachmentChips files={references.slice(0, 3)} /> :
      <p className="tc-muted">{loading ? "Loading files…" : "No reference files yet."}</p>}
    {references.length > 3 && <span className="tc-muted">{references.length - 3} more in Files</span>}
  </section>;
}

export default function TaskFiles({ revisions = [], canDeliver, onOpenWork }: {
  revisions?: Pick<TaskSubmission, "id" | "revisionNumber">[]; canDeliver: boolean; onOpenWork: () => void;
}) {
  const { taskId, currentUserId } = useTaskConversation();
  const { files, loading, error, refresh } = useTaskAttachments();
  const [selected, setSelected] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [failure, setFailure] = useState("");
  const [notice, setNotice] = useState("");
  const guard = useRef(false);
  const beforeUpload = useRef<Set<number> | null>(null);

  const send = async () => {
    if (!selected || guard.current) return;
    guard.current = true; setUploading(true); setFailure(""); setNotice("");
    try {
      if (beforeUpload.current) {
        const latest = await refresh();
        const previousIds = beforeUpload.current;
        const match = latest.some((item) => !previousIds.has(item.id) && item.kind === "Task" && item.fileName === selected.name
          && item.sizeBytes === selected.size && item.uploaderUserId === currentUserId);
        if (match) {
          setNotice("A matching file from you is already in reference files.");
          setSelected(null); beforeUpload.current = null; return;
        }
      }
      const baseline = new Set(files.map((file) => file.id));
      try {
        await uploadTaskAttachment(taskId, selected);
      } catch (reason) {
        if (isAxiosError(reason) && (!reason.response || reason.response.status >= 500)) beforeUpload.current = baseline;
        throw reason;
      }
      setNotice("Reference file uploaded.");
      setSelected(null); beforeUpload.current = null;
    } catch (reason) {
      setFailure(errorMessage(reason, beforeUpload.current ? "Upload could not be confirmed. Retry to check the file list." : "Could not upload the file. Try again."));
    } finally {
      guard.current = false; setUploading(false);
    }
  };

  const delivery = files.filter((item) => item.kind === "Submission");
  const submissionIds = [...new Set(delivery.map((item) => item.taskSubmissionId))];
  return <section className="tc-files" aria-label="Shared files">
    <div className="tc-view-head"><div><h2>Files</h2><p>Shared materials, conversation attachments and submitted work.</p></div>
      <button className="tc-icon-button" type="button" title="Refresh files" aria-label="Refresh files" disabled={loading}
        onClick={() => void refresh().catch(() => undefined)}><CollaborationIcon name="refresh" /></button>
    </div>
    <p className="tc-muted">Uploads here are reference files, not work submitted for review.{" "}
      {canDeliver ? <button type="button" className="tc-link" onClick={onOpenWork}>Open Work for delivery</button>
        : "Completed work is submitted from the Work tab when available."}</p>
    <div className="tc-uploader">
      <FilePicker label="Choose reference file" file={selected} disabled={uploading}
        onChange={(file) => { setSelected(file); setFailure(""); setNotice(""); beforeUpload.current = null; }} />
      {selected ? <button type="button" className="tc-action tc-action--primary" disabled={uploading} onClick={() => void send()}>
        {uploading ? "Uploading…" : "Upload file"}
      </button> : <span className="tc-drop-hint">or drop it on the picker</span>}
    </div>
    {failure && <p role="alert" className="tc-error">{failure}</p>}
    {notice && <p role="status" className="tc-notice">{notice}</p>}
    {error && <p role="alert" className="tc-error">{error} <button className="tc-link" type="button"
      onClick={() => void refresh().catch(() => undefined)}>Retry</button></p>}
    {loading && !files.length && <p className="tc-muted" role="status">Loading files…</p>}
    {!loading && !error && !files.length && <div className="tc-empty"><CollaborationIcon name="file" />
      <div><h3>No shared files yet</h3><p>Add the first reference file above.</p></div></div>}
    {(["Task", "Message"] as const).map((kind) => {
      const group = files.filter((item) => item.kind === kind);
      return group.length > 0 && <section className="tc-file-group" key={kind}>
        <h3>{kind === "Task" ? "Task references" : "Discussion attachments"}</h3>
        <AttachmentChips files={group} detailed />
      </section>;
    })}
    {submissionIds.length > 0 && <section className="tc-file-group"><h3>Submission files</h3>
      {submissionIds.map((id) => {
        const revision = revisions.find((item) => item.id === id);
        return <div className="tc-revision-group" key={id}>
          {revision && <h4>Revision {revision.revisionNumber}</h4>}
          <AttachmentChips files={delivery.filter((item) => item.taskSubmissionId === id)} detailed />
        </div>;
      })}
    </section>}
  </section>;
}
