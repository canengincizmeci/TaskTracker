import { useId, useRef, useState, type FormEvent, type ReactNode, type RefObject } from "react";
import { isAxiosError } from "axios";
import { updateTask } from "../api/taskService";
import { errorMessage } from "../api/errorMessage";
import type { Task } from "../types/task";
import { localCalendarDate, maxTaskCategoryLength } from "../utils/localCalendarDate";
import { useModalFocus } from "../hooks/useModalFocus";

export function TaskDialog({ title, children, busy, onClose, opener, fallback }: {
  title: string; children: ReactNode; busy?: boolean; onClose: () => void; opener?: RefObject<HTMLElement | null>;
  fallback?: RefObject<HTMLElement | null>;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  useModalFocus(ref, opener, fallback);
  return <dialog className="tw-dialog" ref={ref} aria-labelledby={titleId}
    onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}>
    <div className="tw-edit-dialog-head"><h2 id={titleId}>{title}</h2>
      <button type="button" className="tw-control tw-icon-button" aria-label="Close dialog" disabled={busy}
        onClick={onClose}><span aria-hidden="true">×</span></button></div>
    {children}
  </dialog>;
}

const fields = ["title", "description", "category", "priority", "dueDate"] as const;
const names = { title: "Title", description: "Description", category: "Category", priority: "Priority", dueDate: "Due date" };
function taskFields(task: Task) {
  return { title: task.title, description: task.description, category: task.category,
    priority: task.priority, dueDate: task.dueDate?.slice(0, 10) ?? "" };
}

export default function TaskEditDialog({ task, onClose, onSaved, refreshTask, opener }: {
  task: Task; onClose: () => void; onSaved: () => Promise<void>; refreshTask: () => Promise<number | undefined>;
  opener: RefObject<HTMLElement | null>;
}) {
  const [initial] = useState(() => taskFields(task));
  const [draft, setDraft] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [needsRefresh, setNeedsRefresh] = useState(false);
  const sending = useRef(false);
  const latest = taskFields(task);
  // Preserve fields another editor touched when this draft leaves them alone.
  const changed = fields.filter(key => draft[key] !== initial[key]);
  const conflicts = changed.filter(key => latest[key] !== initial[key] && latest[key] !== draft[key]);

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!task.canEdit || sending.current || needsRefresh) return;
    const merged = { ...latest };
    for (const key of changed) merged[key] = draft[key].trim();
    if (merged.title.length < 3 || merged.description.length < 5 || !merged.category) {
      setError("Enter a title of at least 3 characters, a description of at least 5, and a category."); return;
    }
    if (merged.category.length > maxTaskCategoryLength) {
      setError("Category must contain at most 150 characters."); return;
    }
    if (merged.dueDate && merged.dueDate !== latest.dueDate && merged.dueDate < localCalendarDate()) {
      setError("Choose today or a future due date."); return;
    }
    sending.current = true; setBusy(true); setError("");
    try {
      await updateTask({ ...merged, id: task.id, version: task.version, dueDate: merged.dueDate || null });
      await onSaved();
    } catch (reason) {
      if (isAxiosError(reason) && reason.response?.status === 409) {
        setError("The task changed while you were editing. Check the latest values before saving.");
        try { await refreshTask(); } catch { setNeedsRefresh(true); }
      } else {
        setError(errorMessage(reason, "Could not save task details."));
        void refreshTask().catch(() => undefined);
      }
    } finally { sending.current = false; setBusy(false); }
  };
  const change = (key: typeof fields[number], value: string) => {
    setDraft(previous => ({ ...previous, [key]: value })); setError("");
  };
  return <TaskDialog title="Edit task" busy={busy} onClose={onClose} opener={opener}>
    <form className="tw-edit-form" onSubmit={save}>
      <fieldset disabled={busy || !task.canEdit}>
        <label>Title<input required minLength={3} maxLength={100} value={draft.title} onChange={e => change("title", e.target.value)} /></label>
        <label>Description<textarea required minLength={5} maxLength={1000} rows={5} value={draft.description}
          onChange={e => change("description", e.target.value)} /></label>
        <label>Category<input required maxLength={maxTaskCategoryLength} value={draft.category} onChange={e => change("category", e.target.value)} /></label>
        <div className="tw-edit-pair"><label>Priority<select value={draft.priority} onChange={e => change("priority", e.target.value)}>
          {["Low", "Medium", "High", "Critical"].map(value => <option key={value}>{value}</option>)}
        </select></label>
          <label>Due date<input type="date" value={draft.dueDate}
            min={draft.dueDate === latest.dueDate && latest.dueDate < localCalendarDate() ? undefined : localCalendarDate()}
            onChange={e => change("dueDate", e.target.value)} /></label></div>
      </fieldset>
      {conflicts.length > 0 && <div className="tw-notice" role="status">Saving will replace these updated values.
        <dl>{conflicts.map(key => <div key={key}><dt>{names[key]}</dt><dd>{latest[key] || "None"}</dd></div>)}</dl></div>}
      {!task.canEdit && <p className="tw-notice" role="status">This task can no longer be edited. You can still copy your draft.</p>}
      {error && <p className="tw-notice tw-notice--error" role="alert">{error}</p>}
      <div className="tw-dialog-actions"><button type="button" className="tw-control" disabled={busy} onClick={onClose}>Cancel</button>
        {needsRefresh ? <button type="button" className="tw-control" onClick={() => void refreshTask().then(() => setNeedsRefresh(false)).catch(() => setError("Could not refresh. Try again."))}>Reload latest values</button> :
          <button type="submit" className="tw-control tw-control--primary" disabled={busy || !changed.length || !task.canEdit}>
            {busy ? "Saving…" : conflicts.length ? "Save my changes" : "Save changes"}</button>}</div>
    </form>
  </TaskDialog>;
}
