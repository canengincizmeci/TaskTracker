import { useId, useLayoutEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { createSubTask } from "../api/subTaskService";
import { subTaskDetailRefreshKey, subTaskMutationError, subTaskResponseStatus, type SubTaskList } from "../hooks/useSubTasks";
import type { SubTaskFields } from "../types/subTask";
import type { Task } from "../types/task";
import { parentAllowsSubTaskChanges } from "../utils/subTaskWorkflow";
import SubTaskDetail, { SubTaskMetadata } from "./SubTaskDetail";
import { SubTaskFieldsDialog } from "./SubTaskDialogs";
import type { SelectedDiscussionFile } from "./TaskFileControls";
import "../styles/subtasks.css";

export default function SubTasksSection({ task, list, active, disabled, rosterRevision, openCount, refreshParent }: {
  task: Task; list: SubTaskList; active: boolean; disabled: boolean; rosterRevision: number; openCount: number;
  refreshParent: () => Promise<unknown>;
}) {
  const location = useLocation();
  const navigate = useNavigate();
  const label = useId();
  const panel = useId();
  const heading = useRef<HTMLHeadingElement>(null);
  const createButton = useRef<HTMLButtonElement>(null);
  const listElement = useRef<HTMLUListElement>(null);
  const restore = useRef<number | "heading" | null>(null);
  const creating = useRef(false);
  const mounted = useRef(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [createBusy, setCreateBusy] = useState(false);
  const [createError, setCreateError] = useState("");
  const [notice, setNotice] = useState("");
  const [drafts, setDrafts] = useState<Record<number, string>>({});
  const [fileDrafts, setFileDrafts] = useState<Record<number, SelectedDiscussionFile[]>>({});
  const [feedback, setFeedback] = useState<Record<string, string>>({});
  const parentLocked = !parentAllowsSubTaskChanges(task.status);
  const canCreate = task.canManageResponsibility && !parentLocked;
  const selectedParam = new URLSearchParams(location.search).get("subtask");
  const candidate = selectedParam && /^[1-9]\d*$/.test(selectedParam) ? Number(selectedParam) : 0;
  const selectedId = Number.isSafeInteger(candidate) && candidate > 0 ? candidate : null;
  const selected = list.items?.find(item => item.id === selectedId);
  const total = list.items?.length ?? 0;
  const completed = list.items?.filter(item => item.status === "Completed").length ?? 0;

  useLayoutEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  useLayoutEffect(() => {
    if (selectedId == null && restore.current != null) {
      const target = typeof restore.current === "number" ?
        listElement.current?.querySelector<HTMLButtonElement>(`[data-subtask="${restore.current}"]`) : null;
      (target ?? heading.current)?.focus({ preventScroll: true });
      restore.current = null;
    }
  }, [selectedId]);

  function select(id: number | null) {
    const params = new URLSearchParams(location.search);
    if (id == null) params.delete("subtask");
    else { params.set("subtask", String(id)); params.delete("view"); }
    navigate({ pathname: location.pathname, search: params.toString(), hash: "" }, { replace: true, preventScrollReset: true });
  }

  async function create(fields: SubTaskFields, assigneeUserId?: number) {
    if (!canCreate || disabled || list.loading || creating.current) return;
    creating.current = true; setCreateBusy(true); setCreateError(""); setNotice("");
    try {
      const item = await createSubTask(task.id, fields, assigneeUserId);
      if (!mounted.current) return;
      setCreateOpen(false);
      list.reload();
      select(item.id);
      setNotice("Subtask created.");
    } catch (failure) {
      if (!mounted.current) return;
      setCreateError(subTaskMutationError(failure));
      const status = subTaskResponseStatus(failure);
      if (status === 409 || status === 403 || status === 404 || status == null || status >= 500) {
        list.reload();
        void refreshParent().catch(() => undefined);
      }
    } finally {
      creating.current = false;
      if (mounted.current) setCreateBusy(false);
    }
  }

  if (!task.canViewParticipants || (!total && !canCreate && !list.loading && !list.error && !selectedId && !notice)) return null;
  return <section className="st-section" aria-labelledby={label}>
    <header className="st-heading">
      <div><h2 ref={heading} id={label} tabIndex={-1}>Subtasks</h2>
        {total > 0 && <div className="st-progress">
          <progress max={total} value={completed} aria-label="Subtask completion" />
          <span>{completed} of {total} completed</span>
        </div>}</div>
      {canCreate && <button ref={createButton} className="tw-control" type="button" disabled={disabled || list.loading || createBusy}
        onClick={() => { setCreateError(""); setNotice(""); setCreateOpen(true); }}>New subtask</button>}
    </header>
    {notice && <p role="status" className="tw-notice">{notice}</p>}
    {openCount > 0 && task.status !== "Completed" && task.status !== "Cancelled" && <p className="tw-notice">
      The parent task can be completed once all open subtasks are resolved.
      {task.status === "InReview" && task.canReview && " Request changes on the parent to resume subtask work."}
    </p>}
    {parentLocked && total > 0 && <p className="st-muted">Subtasks are read-only while the parent is {task.status === "InReview" ?
      "in review" : task.status.toLowerCase()}.</p>}
    {list.loading && total === 0 && <p role="status" className="st-muted">Loading subtasks…</p>}
    {list.error && <p role="alert" className="tw-notice tw-notice--error">{list.error}
      <button type="button" className="tw-inline-action" onClick={list.reload}>Retry subtasks</button></p>}
    {!total && !list.loading && !list.error && <p className="st-muted">No subtasks yet.</p>}
    {total > 0 && <ul className="st-list" ref={listElement} aria-busy={list.loading}>
      {list.items?.map(item => <li key={item.id} data-selected={item.id === selectedId}>
        <button type="button" className="st-select" data-subtask={item.id}
          aria-expanded={item.id === selectedId} aria-controls={item.id === selectedId ? panel : undefined}
          onClick={() => select(item.id === selectedId ? null : item.id)}>{item.title}</button>
        <SubTaskMetadata item={item} />
      </li>)}
    </ul>}
    {selectedId != null && selected && <SubTaskDetail key={selectedId} task={task} id={selectedId} panelId={panel} active={active}
      refreshKey={subTaskDetailRefreshKey(selected, task.status, task.currentUserPermission, rosterRevision)}
      rosterRevision={rosterRevision}
      parentLocked={parentLocked} disabled={disabled || list.loading || Boolean(list.error)}
      reload={list.reload} refreshParent={refreshParent} draft={drafts[selectedId] ?? ""}
      setDraft={value => setDrafts(current => ({ ...current, [selectedId]: value }))}
      selectedFiles={fileDrafts[selectedId] ?? []}
      setSelectedFiles={value => setFileDrafts(current => ({ ...current, [selectedId]: value }))}
      feedbackFor={revision => feedback[`${selectedId}:${revision}`] ?? ""}
      saveFeedback={(revision, value) => setFeedback(current => ({ ...current, [`${selectedId}:${revision}`]: value }))}
      onClose={() => { restore.current = selectedId; select(null); }}
      onDeleted={() => { restore.current = "heading"; select(null); setNotice("Subtask deleted."); }} />}
    {active && selectedId != null && !selected && !list.loading && !list.error && <p className="tw-notice" role="status">
      This subtask is no longer available.<button className="tw-inline-action" type="button" onClick={() => select(null)}>Dismiss</button></p>}
    {active && createOpen && <SubTaskFieldsDialog assignment={{ task, revision: `${list.key}:${rosterRevision}` }} state={{
      busy: createBusy, blocked: !canCreate || disabled || list.loading,
      error: createError || (!canCreate ? "This task is no longer open for new subtasks." : ""),
      onClose: () => setCreateOpen(false), opener: createButton, fallback: heading,
    }} onSave={(fields, userId) => void create(fields, userId)} />}
  </section>;
}
