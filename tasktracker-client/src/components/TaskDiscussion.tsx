import { useLayoutEffect, useRef, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { getTaskMessages, sendTaskMessage } from "../api/taskCollaborationService";
import { errorMessage } from "../api/errorMessage";
import { uploadMessageAttachment } from "../api/taskAttachmentService";
import { useTaskConversation } from "../hooks/useTaskConversation";
import { useTaskAttachments } from "../hooks/useTaskAttachments";
import { AttachmentChips, CollaborationIcon, FilePicker } from "./TaskFileControls";

export default function TaskDiscussion() {
  const { taskId, currentUserId, messages, messageError, liveError, loading, addMessage, reload } = useTaskConversation();
  const { files, error: fileError, refresh } = useTaskAttachments();
  const [content, setContent] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState<"sending" | "attaching" | "checking" | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [pending, setPending] = useState<{ messageId: number; file: File } | null>(null);
  const [uncertain, setUncertain] = useState<Set<number> | null>(null);
  const guard = useRef(false);
  const composer = useRef<HTMLTextAreaElement>(null);
  const feed = useRef<HTMLOListElement>(null);
  const following = useRef(true);
  const newest = messages.at(-1)?.id;
  useLayoutEffect(() => {
    const list = feed.current;
    if (!list) return;
    const follow = () => {
      if (following.current && list.clientHeight) list.scrollTop = list.scrollHeight;
    };
    follow();
    const observer = new ResizeObserver(follow);
    observer.observe(list);
    return () => observer.disconnect();
  }, [newest]);

  async function attach(messageId: number, chosen: File, retry: boolean) {
    setBusy("attaching");
    try {
      if (retry) {
        const latest = await refresh();
        if (latest.some((item) => item.taskMessageId === messageId && item.fileName === chosen.name && item.sizeBytes === chosen.size)) {
          setPending(null); setNotice("File is attached to your message."); return;
        }
      }
      await uploadMessageAttachment(taskId, messageId, chosen);
      setPending(null); setNotice("Message and file shared.");
    } catch (reason) {
      setPending({ messageId, file: chosen });
      setError(errorMessage(reason, "File upload could not be confirmed."));
    }
  }

  async function send(event: FormEvent) {
    event.preventDefault();
    if (guard.current || !content.trim() || pending || uncertain) return;
    const baseline = new Set(messages.map((item) => item.id));
    guard.current = true; setBusy("sending"); setError(""); setNotice("");
    try {
      const message = await sendTaskMessage(taskId, content.trim());
      following.current = true;
      addMessage(message); setContent(""); setFile(null);
      if (file) await attach(message.id, file, false);
      else setNotice("Message sent.");
    } catch (reason) {
      if (isAxiosError(reason) && (!reason.response || reason.response.status >= 500)) {
        setUncertain(baseline);
        setError("Delivery could not be confirmed. Check the conversation before sending again.");
      } else setError(errorMessage(reason, "Could not send message. Try again."));
    } finally {
      guard.current = false; setBusy(null);
    }
  }

  async function checkDelivery() {
    if (!uncertain || guard.current) return;
    guard.current = true; setBusy("checking"); setError("");
    try {
      const latest = await getTaskMessages(taskId);
      latest.forEach(addMessage);
      const matches = latest.filter((item) => !uncertain.has(item.id) && item.senderUserId === currentUserId && item.content === content.trim());
      if (matches.length === 1) {
        setContent(""); setFile(null);
        if (file) setPending({ messageId: matches[0].id, file });
        setNotice("A matching message from you is already in the conversation.");
      } else setNotice("Conversation refreshed. Check your message before sending again.");
      setUncertain(null);
    } catch (reason) {
      setError(errorMessage(reason, "Could not check the conversation. Try again."));
    } finally {
      guard.current = false; setBusy(null);
    }
  }

  async function retryFile() {
    if (!pending || guard.current) return;
    guard.current = true; setError(""); setNotice("");
    try { await attach(pending.messageId, pending.file, true); }
    finally { guard.current = false; setBusy(null); }
  }

  return <section className="tc-discussion" aria-label="Task discussion">
    <div className="tc-view-head"><div><h2>Discussion</h2><p>Questions and updates from the people working on this task.</p></div>
      <button className="tc-link" type="button" disabled={!!busy || !!uncertain}
        onClick={() => composer.current?.focus()}>Write a message</button>
      <button className="tc-icon-button" type="button" aria-label="Refresh discussion" title="Refresh discussion" disabled={loading}
        onClick={() => { void reload(); void refresh().catch(() => undefined); }}><CollaborationIcon name="refresh" /></button>
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
        </header><p>{item.content}</p>
          <AttachmentChips files={files.filter((attachment) => attachment.taskMessageId === item.id)} />
        </article>
      </li>)}
    </ol>}
    {pending && <div className="tc-pending-file" role="group" aria-label="Message file upload">
      <strong>Your message is saved</strong><p>Attach <b>{pending.file.name}</b> to your saved message.</p>
      <div><button className="tc-action" type="button" disabled={!!busy} onClick={() => void retryFile()}>
        {busy === "attaching" ? "Checking file…" : "Retry attachment"}</button>
        <button type="button" className="tc-link" disabled={!!busy}
          onClick={() => { setPending(null); setError(""); setNotice(""); composer.current?.focus(); }}>Dismiss file</button></div>
    </div>}
    <form className="tc-composer" onSubmit={send}>
      <label htmlFor="workspace-message">Message</label>
      <textarea ref={composer} id="workspace-message" rows={3} maxLength={4000} value={content}
        disabled={!!busy || !!uncertain} placeholder="Write a message…"
        onChange={(event) => { setContent(event.target.value); setNotice(""); if (!pending) setError(""); }} />
      <div className="tc-composer-tools">
        <FilePicker file={file} label="Attach file" disabled={!!busy || !!uncertain}
          onChange={(value) => { setFile(value); setNotice(""); }} />
        {uncertain ? <button className="tc-action tc-action--primary" type="button" disabled={!!busy} onClick={() => void checkDelivery()}>
          {busy ? "Checking…" : "Check sent messages"}</button> :
          <button className="tc-action tc-action--primary" type="submit" disabled={!!busy || !content.trim() || !!pending}>
            <CollaborationIcon name="send" />{busy === "sending" ? "Sending…" : busy === "attaching" ? "Attaching…" : "Send"}
          </button>}
      </div>
    </form>
    {error && <p role="alert" className="tc-error">{error}</p>}
    {notice && <p role="status" className="tc-notice">{notice}</p>}
  </section>;
}
