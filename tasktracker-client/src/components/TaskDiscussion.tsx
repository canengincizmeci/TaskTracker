import { useRef, useState, type FormEvent } from "react";
import { sendTaskMessage } from "../api/taskWorkspaceService";
import { errorMessage } from "../api/errorMessage";
import type { TaskMessage } from "../types/taskWorkspace";

export default function TaskDiscussion({ taskId, messages, loading, error, onMessage }: {
  taskId: number;
  messages: TaskMessage[];
  loading: boolean;
  error: string;
  onMessage: (message: TaskMessage) => void;
}) {
  const [content, setContent] = useState("");
  const [sending, setSending] = useState(false);
  const [sendError, setSendError] = useState("");
  const busy = useRef(false);

  const send = async (event: FormEvent) => {
    event.preventDefault();
    if (busy.current || !content.trim()) return;
    busy.current = true;
    setSending(true);
    setSendError("");
    try {
      onMessage(await sendTaskMessage(taskId, content));
      setContent("");
    } catch (reason: unknown) {
      setSendError(errorMessage(reason, "Could not send message."));
    } finally {
      busy.current = false;
      setSending(false);
    }
  };

  return <section className="task-detail-section" aria-label="Discussion">
    <h2>Discussion</h2>
    <p>Latest 100 messages, oldest first.</p>
    {error && <p role="alert" className="error-message">{error}</p>}
    {loading && <p>Loading discussion...</p>}
    {!loading && !error && messages.length === 0 && <p>No messages yet.</p>}
    <ol className="activity-timeline">
      {messages.map((message) => <li className="timeline-item" key={message.id}>
        <strong>{message.senderUserName}</strong>
        <time dateTime={message.createdAt}>{new Date(message.createdAt).toLocaleString()}</time>
        <p style={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{message.content}</p>
      </li>)}
    </ol>
    <form onSubmit={send} className="auth-form">
      <label htmlFor="task-message">Message</label>
      <textarea id="task-message" rows={3} maxLength={4000} value={content} disabled={sending}
        onChange={(event) => setContent(event.target.value)} />
      {sendError && <p role="alert" className="error-message">{sendError}</p>}
      <button className="primary-button" type="submit" disabled={sending || !content.trim()}>
        {sending ? "Sending..." : "Send"}
      </button>
    </form>
  </section>;
}
