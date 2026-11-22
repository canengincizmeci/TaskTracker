import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { deliverTaskMessage, getDiscussionAttachmentSettings } from "../api/taskCollaborationService";
import { errorMessage } from "../api/errorMessage";
import { useTaskConversation } from "../hooks/useTaskConversation";
import { useTaskAttachments } from "../hooks/useTaskAttachments";
import type { DiscussionAttachmentSettings } from "../types/taskCollaboration";
import { AttachmentChips, CollaborationIcon, DiscussionFilePicker, type SelectedDiscussionFile } from "./TaskFileControls";

function newDeliveryId() {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const id = Array.from(bytes, value => value.toString(16).padStart(2, "0")).join("");
  return `${id.slice(0, 8)}-${id.slice(8, 12)}-${id.slice(12, 16)}-${id.slice(16, 20)}-${id.slice(20)}`;
}

export default function TaskDiscussion({ active = true }: { active?: boolean }) {
  const { taskId, messages, messageError, liveError, loading, addMessage, reload, onAccessRevoked } = useTaskConversation();
  const { files, error: fileError, refresh, refreshFresh } = useTaskAttachments();
  const [content, setContent] = useState("");
  const [selected, setSelected] = useState<SelectedDiscussionFile[]>([]);
  const [settings, setSettings] = useState<DiscussionAttachmentSettings>();
  const [settingsError, setSettingsError] = useState("");
  const [settingsRevision, setSettingsRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState<number>();
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retryId, setRetryId] = useState<string>();
  const sending = useRef<AbortController | null>(null);
  const composer = useRef<HTMLTextAreaElement>(null);
  const feed = useRef<HTMLOListElement>(null);
  const following = useRef(true);
  const newest = messages.at(-1)?.id;

  useLayoutEffect(() => {
    const list = feed.current;
    if (!list || !active) return;
    const follow = () => {
      if (following.current && list.clientHeight) list.scrollTop = list.scrollHeight;
    };
    follow();
    if (typeof ResizeObserver !== "function") return;
    const observer = new ResizeObserver(follow);
    observer.observe(list);
    return () => observer.disconnect();
  }, [active, newest]);

  useEffect(() => {
    if (!active) return;
    const request = new AbortController();
    getDiscussionAttachmentSettings(taskId, request.signal).then(value => {
      if (!request.signal.aborted) { setSettings(value); setSettingsError(""); }
    }).catch(reason => {
      if (request.signal.aborted) return;
      setSettingsError("Attachment availability could not be checked.");
      if (isAxiosError(reason) && reason.response?.status === 403) onAccessRevoked();
    });
    return () => request.abort();
  }, [active, onAccessRevoked, settingsRevision, taskId]);

  useEffect(() => () => sending.current?.abort(), []);

  const canSend = Boolean(content.trim() || selected.length) &&
    (!selected.length || Boolean(settings?.uploadsEnabled) || Boolean(retryId));
  async function send(event: FormEvent) {
    event.preventDefault();
    if (!active || sending.current || !canSend) return;
    const id = retryId ?? newDeliveryId();
    const request = new AbortController();
    sending.current = request;
    setBusy(true); setProgress(undefined); setError(""); setNotice("");
    try {
      const message = await deliverTaskMessage(taskId, id, content.trim(), selected.map(item => item.file), request.signal, percent => {
        if (!request.signal.aborted) setProgress(percent);
      });
      if (request.signal.aborted) return;
      following.current = true;
      addMessage(message); setContent(""); setSelected([]); setRetryId(undefined);
      setNotice(selected.length ? "Message and attachments sent." : "Message sent.");
      if (selected.length) void refreshFresh().catch(() => undefined);
    } catch (reason) {
      if (request.signal.aborted) return;
      const response = isAxiosError<{ code?: string }>(reason) ? reason.response : undefined;
      if (response?.status === 403) {
        onAccessRevoked();
        return;
      }
      const knownUnavailable = response?.data?.code === "discussion_upload_unavailable" ||
        response?.data?.code === "file_scan_unavailable";
      if (!response || (response.status >= 500 && !knownUnavailable)) {
        setRetryId(id);
        setError("Delivery could not be confirmed. Retry this delivery to recover it without posting twice.");
      } else {
        const message = errorMessage(reason, response.status === 413 ? "The attachments exceed the upload size limit." : "Could not send the message.");
        setError(retryId ? `${message} Retry the original delivery when available.` : message);
      }
    } finally {
      if (sending.current === request) sending.current = null;
      if (!request.signal.aborted) setBusy(false);
    }
  }

  const messageFiles = new Map<number, typeof files>();
  for (const file of files) {
    if (file.kind !== "Message" || file.taskMessageId == null) continue;
    const bucket = messageFiles.get(file.taskMessageId);
    if (bucket) bucket.push(file); else messageFiles.set(file.taskMessageId, [file]);
  }

  return <section className="tc-discussion" aria-label="Task discussion">
    <div className="tc-view-head"><div><h2>Discussion</h2><p>Questions and updates from the people working on this task.</p></div>
      <button className="tc-link" type="button" disabled={busy || !!retryId}
        onClick={() => composer.current?.focus()}>Write a message</button>
      <button className="tc-icon-button" type="button" aria-label="Refresh discussion" title="Refresh discussion" disabled={loading}
        onClick={() => { void reload(); void refresh().catch(() => undefined); setSettingsRevision(value => value + 1); }}>
        <CollaborationIcon name="refresh" /></button>
    </div>
    {liveError && <p className="tc-status" role="status">{liveError}</p>}
    {messageError && <p className="tc-error" role="alert">{messageError}</p>}
    {fileError && <p className="tc-error" role="alert">{fileError} <button type="button" className="tc-link"
      onClick={() => void refresh().catch(() => undefined)}>Retry attachments</button></p>}
    {loading && !messages.length && <p className="tc-muted" role="status">Loading discussion…</p>}
    {!loading && !messageError && !messages.length && <div className="tc-empty">
      <CollaborationIcon name="discussion" /><div><h3>Start the conversation</h3><p>Share an update or ask your first question.</p></div>
    </div>}
    {messages.length > 0 && <ol ref={feed} className="tc-messages" aria-label="Latest 100 messages, oldest first" tabIndex={0}
      onScroll={(event) => {
        const list = event.currentTarget;
        following.current = list.scrollHeight - list.scrollTop - list.clientHeight < 40;
      }}>
      {messages.map((item) => <li key={item.id} className="tc-message" id={`discussion-message-${item.id}`}>
        <span aria-hidden="true" className="tc-avatar">{[...item.senderUserName.trim()][0]?.toUpperCase() || "·"}</span>
        <article><header><strong>{item.senderUserName}</strong>
          <time dateTime={item.createdAt}>{new Date(item.createdAt).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })}</time>
        </header>{item.content && <p>{item.content}</p>}
          <AttachmentChips files={messageFiles.get(item.id) ?? []} previewImages={active} />
          {(item.deliveryFileCount ?? 0) > (messageFiles.get(item.id)?.length ?? 0) &&
            <p className="tc-muted">{fileError ? "Attachments unavailable." : "Loading attachments…"}</p>}
        </article>
      </li>)}
    </ol>}
    <form className="tc-composer" onSubmit={send}>
      <label htmlFor="workspace-message">Message</label>
      <textarea ref={composer} id="workspace-message" rows={3} maxLength={4000} value={content}
        disabled={busy || !!retryId} placeholder="Write a message or attach files…"
        onChange={(event) => { setContent(event.target.value); setNotice(""); setError(""); }} />
      <div className="tc-composer-tools">
        <div className="tc-discussion-picker">
          {settings && (settings.uploadsEnabled || selected.length > 0) && <DiscussionFilePicker files={selected} settings={settings}
            disabled={busy || !!retryId} onChange={value => { setSelected(value); setError(""); setNotice(""); }} />}
          {settings && !settings.uploadsEnabled && <p className="tc-muted">Discussion file uploads are unavailable.</p>}
          {settingsError && <p className="tc-error" role="alert">{settingsError} <button type="button" className="tc-link"
            onClick={() => setSettingsRevision(value => value + 1)}>Retry check</button></p>}
        </div>
        <button className="tc-action tc-action--primary" type="submit" disabled={busy || !canSend}>
          <CollaborationIcon name="send" />{busy ? "Sending…" : retryId ? "Retry delivery" : "Send"}
        </button>
      </div>
      {busy && selected.length > 0 && <div className="tc-upload-progress">
        <progress value={progress} max={100} aria-label="Attachment upload progress" />
        <span role="status">{progress === 100 ? "Checking files and saving message…" :
          progress == null ? "Uploading attachments…" : `Uploading attachments… ${progress}%`}</span>
      </div>}
    </form>
    {error && <p role="alert" className="tc-error">{error}</p>}
    {notice && <p role="status" className="tc-notice">{notice}</p>}
  </section>;
}
