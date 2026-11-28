import { useCallback, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { isAxiosError } from "axios";
import { deleteTask, taskAction } from "../api/taskService";
import { errorMessage } from "../api/errorMessage";
import { useTaskSubmissionWorkflow } from "../hooks/useTaskSubmissionWorkflow";
import { useTaskConversation } from "../hooks/useTaskConversation";
import { useSubTaskList } from "../hooks/useSubTasks";
import { taskStatusLabel } from "../utils/taskDisplay";
import { isOpenSubTask } from "../utils/subTaskWorkflow";
import type { Task } from "../types/task";
import TaskResponsibility from "./TaskResponsibility";
import TaskSubmissionPanel, { TaskSubmissionHistory } from "./TaskSubmissionPanel";
import TaskDiscussion from "./TaskDiscussion";
import TaskFiles, { TaskReferenceFiles } from "./TaskFiles";
import { TaskWorkspaceActivity } from "./TaskCollaborationPanel";
import TaskEditDialog, { TaskDialog } from "./TaskEditDialog";
import TaskMlConsentDialog from "./TaskMlConsentDialog";
import SubTasksSection from "./SubTasksSection";

import { tabs, labels, type View } from "../utils/taskWorkspaceViews";

const icons = {
  work: "M4 4h16v16H4z M8 9h8 M8 13h5", discussion: "M4 4h16v12H9l-5 4z",
  files: "M3 7V5h7l2 3h9v11H3z", activity: "M3 12h4l3-7 4 14 3-7h4",
  back: "m13 5-7 7 7 7 M6 12h15", calendar: "M4 6h16v15H4z M8 3v5 M16 3v5 M4 11h16",
  priority: "M5 18v-4 M11 18V9 M17 18V4", edit: "m5 15-1 5 5-1L20 8l-4-4z M13 7l4 4",
  more: "M4 12h1 M11 12h1 M18 12h1",
};
function Icon({ name }: { name: keyof typeof icons }) {
  return <svg className="tw-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={name === "more" ? 3 : 1.7}
    strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={icons[name]} /></svg>;
}

type Command = "start" | "complete" | "cancel" | "reopen" | "delete";
const notices = { start: "Work started.", complete: "Task completed.", cancel: "Task cancelled.", reopen: "Task reopened." };
const confirmations = {
  delete: ["Delete task?", "The task will be removed from active work.", "Delete task"],
  cancel: ["Cancel this task?", "Work on this task will stop. You can reopen it later.", "Cancel task"],
  reopen: ["Reopen this task?", "The task will return to Not started. Its recorded activity stays available.", "Reopen task"],
};

export default function TaskWorkspace({ task, view, refreshTask, refreshFailed }: {
  task: Task; view: View; refreshTask: () => Promise<number | undefined>; refreshFailed: boolean;
}) {
  const navigate = useNavigate();
  const location = useLocation();
  const [editing, setEditing] = useState(false);
  const [consentOpen, setConsentOpen] = useState(false);
  const [confirmation, setConfirmation] = useState<{ command: keyof typeof confirmations; version: number } | null>(null);
  const confirm = confirmation?.command;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState<{ message: string; version: number } | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const lock = useRef(false);
  const alive = useRef(true);
  const menu = useRef<HTMLDivElement>(null);
  const moreButton = useRef<HTMLButtonElement>(null);
  const editButton = useRef<HTMLButtonElement>(null);
  const menuEnter = useRef(false);
  const jumpToSubmission = useRef<"submit" | "review" | null>(null);
  const tabList = useRef<HTMLDivElement>(null);
  const collaborative = Boolean(task.canViewParticipants);
  const { taskRevision, onAccessRevoked, rosterRevision } = useTaskConversation();
  const subTasks = useSubTaskList(task.id, collaborative, taskRevision, onAccessRevoked, refreshTask);
  const openSubTasks = subTasks.items?.filter(item => isOpenSubTask(item.status)).length ?? 0;
  const completionBlocked = collaborative && (openSubTasks > 0 || subTasks.loading || Boolean(subTasks.error));
  const reloadSubTasks = subTasks.reload;
  const refreshWork = useCallback(() => {
    reloadSubTasks();
    return refreshTask();
  }, [refreshTask, reloadSubTasks]);
  const clearFeedback = useCallback(() => { setNotice(null); setError(""); }, []);
  const workflow = useTaskSubmissionWorkflow(task, refreshWork, clearFeedback);
  const blocked = busy || workflow.busy || refreshFailed;
  const back = task.workspaceId != null ? `/workspaces/${task.workspaceId}` : "/dashboard";
  const availableTabs = collaborative ? tabs : (["work"] as const);

  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useLayoutEffect(() => {
    if (menuOpen && menuEnter.current) {
      menu.current?.querySelector<HTMLButtonElement>(".tw-menu button:not(:disabled)")?.focus();
      menuEnter.current = false;
    }
  }, [menuOpen]);
  useLayoutEffect(() => {
    if (view !== "work" || !jumpToSubmission.current) return;
    const intent = jumpToSubmission.current;
    jumpToSubmission.current = null;
    const target = (intent === "review" ? document.getElementById("delivery-heading")
      : document.querySelector<HTMLElement>("#submission-content:not(:disabled), #view-work .wf-add-file summary"))
      ?? document.getElementById("wf-next-heading");
    target?.focus();
  }, [view]);
  useEffect(() => {
    if (!menuOpen) return;
    const outside = (event: PointerEvent) => {
      if (event.target instanceof Node && !menu.current?.contains(event.target)) setMenuOpen(false);
    };
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, [menuOpen]);

  function selectView(next: View) {
    if (next === view) return;
    const params = new URLSearchParams(location.search);
    if (next === "work") params.delete("view"); else params.set("view", next);
    navigate({ pathname: location.pathname, search: params.toString(), hash: "" }, { preventScrollReset: true });
  }

  function tabKey(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const count = availableTabs.length;
    const target = event.key === "ArrowRight" ? (index + 1) % count : event.key === "ArrowLeft" ?
      (index - 1 + count) % count : event.key === "Home" ? 0 : event.key === "End" ? count - 1 : -1;
    if (target < 0) return;
    event.preventDefault(); selectView(availableTabs[target]);
    tabList.current?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[target]?.focus();
  }

  async function showSaved(message: string) {
    const version = await refreshTask().catch(() => undefined);
    if (alive.current) setNotice({ version: version ?? task.version, message });
  }
  async function execute(command: Command) {
    if (lock.current || blocked || editing || (command === "complete" && completionBlocked)) return;
    if (confirmation && confirmation.version !== task.version) {
      setConfirmation(null);
      setError("The task changed. Check its current state before choosing this action again.");
      return;
    }
    lock.current = true; setBusy(true); clearFeedback();
    try {
      if (command === "delete") await deleteTask(task.id);
      else await taskAction(task.id, command, task.version);
      if (!alive.current) return;
      if (command === "delete") { navigate(back, { replace: true }); return; }
      setConfirmation(null);
      await showSaved(notices[command]);
    } catch (reason) {
      if (!alive.current) return;
      setConfirmation(null);
      const uncertain = isAxiosError(reason) && (!reason.response || reason.response.status >= 500);
      setError(uncertain ? "The server could not confirm this action. Check the current task state before retrying."
        : errorMessage(reason, "The task action could not be completed."));
      await refreshWork().catch(() => undefined);
    } finally {
      lock.current = false;
      if (alive.current) setBusy(false);
    }
  }
  function ask(command: keyof typeof confirmations) {
    setMenuOpen(false); moreButton.current?.focus(); setConfirmation({ command, version: task.version });
  }

  function menuKey(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape" && menuOpen) {
      event.preventDefault();
      setMenuOpen(false);
      moreButton.current?.focus();
    } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (!menuOpen) { menuEnter.current = true; setMenuOpen(true); return; }
      const actions = Array.from(menu.current?.querySelectorAll<HTMLButtonElement>(".tw-menu button:not(:disabled)") ?? []);
      if (!actions.length) return;
      const current = actions.findIndex(action => action === document.activeElement);
      const offset = event.key === "ArrowDown" ? 1 : -1;
      actions[(current < 0 && offset < 0 ? actions.length - 1 : (current + offset + actions.length) % actions.length)].focus();
    }
  }

  const terminal = task.status === "Completed" || task.status === "Cancelled";
  const date = task.dueDate?.slice(0, 10);
  return <main className="task-detail-page tw-page"><div className="tw-canvas">
    <header className="tw-header">
      <div className="tw-utility"><Link to={back} className="tw-back"><Icon name="back" />
        {task.workspaceId != null ? task.workspaceName ?? "Workspace" : "My Work"}</Link>
        <div className="tw-tools">{task.canEdit && <button ref={editButton} className="tw-control" type="button" disabled={blocked}
          onClick={() => { clearFeedback(); setEditing(true); }}><Icon name="edit" />Edit task</button>}
          {(task.isOwner || task.canDelete) && <div ref={menu} className="tw-more" onBlur={event => {
            if (!event.currentTarget.contains(event.relatedTarget)) setMenuOpen(false);
          }} onKeyDown={menuKey}>
            <button ref={moreButton} className="tw-control tw-icon-button" type="button" aria-label="More task actions"
              aria-expanded={menuOpen} aria-controls="workspace-actions" onClick={() => setMenuOpen(!menuOpen)}><Icon name="more" /></button>
            {menuOpen && <div id="workspace-actions" className="tw-menu">
              {task.mlConsent && <button type="button" lang="tr" disabled={blocked} onClick={() => {
                setMenuOpen(false); clearFeedback(); setConsentOpen(true);
              }}>Veri kullanım izni</button>}
              {task.isOwner && <button type="button" disabled={blocked} onClick={() => ask(terminal ? "reopen" : "cancel")}>
                {terminal ? "Reopen task" : "Cancel task"}</button>}
              {task.canDelete && <button type="button" className="tw-danger" disabled={blocked} onClick={() => ask("delete")}>Delete task</button>}
            </div>}</div>}</div>
      </div>
      <div className="tw-context"><span className={`tw-status tw-status--${task.status.toLowerCase()}`}>{taskStatusLabel(task.status)}</span>
        {task.category && <span>{task.category}</span>}</div>
      <h1>{task.title}</h1>
      <div className="tw-properties">
        <TaskResponsibility task={task} onChanged={refreshTask} disabled={blocked || editing} />
        <span className="tw-meta" title="Due date"><Icon name="calendar" />
          {date ? <time dateTime={date}>Due {new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(new Date(date + "T00:00:00"))}</time> : "No due date"}</span>
        <span className="tw-meta"><Icon name="priority" />{task.priority} priority</span>
      </div>
    </header>

    <div className="tw-tabbar" role="tablist" aria-label="Task views" ref={tabList}>
      {availableTabs.map((tab, index) => <button type="button" role="tab" id={`tab-${tab}`} key={tab}
        aria-controls={`view-${tab}`} aria-selected={tab === view} tabIndex={tab === view ? 0 : -1}
        onKeyDown={event => tabKey(event, index)} onClick={() => selectView(tab)}><Icon name={tab} />{labels[tab]}</button>)}
    </div>

    {notice?.version === task.version && <p className="tw-notice" role="status">{notice.message}</p>}
    {error && <p className="tw-notice tw-notice--error" role="alert">{error}</p>}
    {refreshFailed && <div className="tw-notice tw-notice--error" role="alert">The latest task details could not be loaded.
      <button type="button" className="tw-inline-action" onClick={() => void refreshTask().catch(() => undefined)}>Reload task</button></div>}

    <section id="view-work" className="tw-panel" role="tabpanel" aria-labelledby="tab-work" tabIndex={0} hidden={view !== "work"}>
      <TaskSubmissionPanel task={task} workflow={workflow} statusBusy={busy || editing || refreshFailed} completionBlocked={completionBlocked}
        onOpenHistory={() => { selectView("activity"); document.getElementById("tab-activity")?.focus(); }}
        onStart={() => execute("start")} onComplete={() => execute("complete")}>
        <section className="tw-description" aria-labelledby="task-description-title"><h2 id="task-description-title">Description</h2>
          <p>{task.description}</p></section>
        {collaborative && <SubTasksSection task={task} list={subTasks} active={view === "work"} disabled={blocked || editing}
          rosterRevision={rosterRevision} openCount={openSubTasks} refreshParent={refreshTask} />}
        {collaborative && <TaskReferenceFiles onOpenFiles={() => { selectView("files"); document.getElementById("tab-files")?.focus(); }} />}
      </TaskSubmissionPanel>
    </section>
    {collaborative && <>
      <section id="view-discussion" className="tw-panel" role="tabpanel" aria-labelledby="tab-discussion" tabIndex={0} hidden={view !== "discussion"}><TaskDiscussion active={view === "discussion"} /></section>
      <section id="view-files" className="tw-panel" role="tabpanel" aria-labelledby="tab-files" tabIndex={0} hidden={view !== "files"}>
        <TaskFiles revisions={workflow.submissions} canDeliver={task.canSubmit || workflow.canAttach}
          onOpenWork={() => { jumpToSubmission.current = "submit"; selectView("work"); }} />
      </section>
      <section id="view-activity" className="tw-panel" role="tabpanel" aria-labelledby="tab-activity" tabIndex={0} hidden={view !== "activity"}>
        <TaskWorkspaceActivity /><TaskSubmissionHistory task={task} workflow={workflow}
          onOpenCurrent={() => { jumpToSubmission.current = "review"; selectView("work"); }} />
      </section>
    </>}
    {editing && <TaskEditDialog task={task} opener={editButton} onClose={() => setEditing(false)} refreshTask={refreshTask}
      onSaved={async () => {
        await showSaved("Task details saved.");
        if (alive.current) setEditing(false);
      }} />}
    {consentOpen && task.mlConsent && <TaskMlConsentDialog task={task} opener={moreButton}
      onClose={() => setConsentOpen(false)} refreshTask={refreshTask} onSaved={async () => {
        await showSaved("Veri kullanım izni kaydedildi.");
        if (alive.current) setConsentOpen(false);
      }} />}
    {confirm && <TaskDialog title={confirmations[confirm][0]} onClose={() => setConfirmation(null)} busy={busy} opener={moreButton}>
      <p>{confirmations[confirm][1]}</p><div className="tw-dialog-actions">
        <button className="tw-control" type="button" disabled={busy} onClick={() => setConfirmation(null)}>Go back</button>
        <button type="button" className={`tw-control ${confirm === "reopen" ? "tw-control--primary" : "tw-control--danger"}`}
          disabled={blocked} onClick={() => execute(confirm)}>{busy ? "Saving…" : confirmations[confirm][2]}</button></div>
    </TaskDialog>}
  </div></main>;
}
