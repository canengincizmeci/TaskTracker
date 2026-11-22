import { useEffect, useState, type RefObject } from "react";
import { getParticipants } from "../api/taskCollaborationService";
import { getWorkspaceMembers } from "../api/workspaceService";
import type { Task } from "../types/task";
import { subTaskPriorities, type SubTask, type SubTaskFields } from "../types/subTask";
import { localCalendarDate } from "../utils/localCalendarDate";
import { TaskDialog } from "./TaskEditDialog";

export type SubTaskDialogState = {
  busy: boolean;
  blocked: boolean;
  error: string;
  onClose: () => void;
  opener: RefObject<HTMLElement | null>;
  fallback: RefObject<HTMLElement | null>;
};

function Actions({ state, label, allowed = true, danger = false }: {
  state: SubTaskDialogState; label: string; allowed?: boolean; danger?: boolean;
}) {
  return <>
    {state.error && <p role="alert" className="tw-notice tw-notice--error">{state.error}</p>}
    <div className="tw-dialog-actions">
      <button type="button" className="tw-control" disabled={state.busy} onClick={state.onClose}>Go back</button>
      <button type="submit" className={`tw-control tw-control--${danger ? "danger" : "primary"}`}
        disabled={state.busy || state.blocked || !allowed}>{state.busy ? "Saving…" : label}</button>
    </div>
  </>;
}

export function SubTaskFieldsDialog({ item, state, onSave, assignment }: {
  item?: SubTask; state: SubTaskDialogState; onSave: (fields: SubTaskFields, assigneeUserId?: number) => void;
  assignment?: { task: Task; revision: string };
}) {
  const [fields, setFields] = useState<SubTaskFields>(() => ({
    title: item?.title ?? "", description: item?.description ?? "",
    priority: item?.priority ?? "Medium", dueDate: item?.dueDate ?? null,
  }));
  const set = <K extends keyof SubTaskFields>(key: K, value: SubTaskFields[K]) =>
    setFields(current => ({ ...current, [key]: value }));
  const today = localCalendarDate();
  const [choice, setChoice] = useState("");
  const { candidates, retry } = useEligibleAssignees(assignment?.task, assignment?.revision ?? "");
  const validChoice = !choice || candidates?.some(person => String(person.userId) === choice);
  const oldDate = fields.dueDate === item?.dueDate && fields.dueDate != null && fields.dueDate < today;
  const valid = Boolean(validChoice && fields.title.trim() && fields.description.trim() &&
    (!fields.dueDate || oldDate || fields.dueDate >= today));
  return <TaskDialog title={item ? "Edit subtask" : "Create subtask"} {...state}>
    <form className="tw-edit-form" onSubmit={event => {
      event.preventDefault();
      if (!state.busy && !state.blocked && valid)
        onSave({ ...fields, title: fields.title.trim(), description: fields.description.trim() }, choice ? Number(choice) : undefined);
    }}>
      <fieldset disabled={state.busy}>
        <label>Title<input required autoFocus maxLength={150} value={fields.title} onChange={event => set("title", event.target.value)} /></label>
        <label>Description<textarea required rows={4} maxLength={10000} value={fields.description}
          onChange={event => set("description", event.target.value)} /></label>
        <div className="tw-edit-pair">
          <label>Priority<select value={fields.priority} onChange={event => {
            const value = subTaskPriorities.find(priority => priority === event.target.value);
            if (value) set("priority", value);
          }}>{subTaskPriorities.map(value => <option key={value}>{value}</option>)}</select></label>
          <label>Due date<input type="date" min={oldDate ? undefined : today} value={fields.dueDate ?? ""}
            onChange={event => set("dueDate", event.target.value || null)} /></label>
        </div>
        {assignment && <AssigneeChoices choice={choice} setChoice={setChoice} candidates={candidates} retry={retry} task={assignment.task} />}
      </fieldset>
      <Actions state={state} label={item ? "Save changes" : "Create subtask"} allowed={valid} />
    </form>
  </TaskDialog>;
}

type Candidate = { userId: number; userName: string };
function useEligibleAssignees(task: Task | undefined, revision: string) {
  const [attempt, retry] = useState(0);
  const [roster, setRoster] = useState<{ key: string; candidates: Candidate[] | null }>();
  const id = task?.id;
  const workspaceId = task?.workspaceId;
  const ownerId = task?.ownerId;
  const ownerUserName = task?.ownerUserName;
  const key = `${revision}:${attempt}`;
  useEffect(() => {
    if (id == null || ownerId == null || ownerUserName == null) return;
    let active = true;
    const query = workspaceId != null ? getWorkspaceMembers(workspaceId) : getParticipants(id).then(participants => [
      { userId: ownerId, userName: ownerUserName },
      ...participants.filter(person => person.userId !== ownerId && (person.permission === "Edit" || person.permission === "Manage")),
    ]);
    query.then(candidates => { if (active) setRoster({ key, candidates }); })
      .catch(() => { if (active) setRoster({ key, candidates: null }); });
    return () => { active = false; };
  }, [id, key, ownerId, ownerUserName, workspaceId]);
  const candidates = roster?.key === key ? roster.candidates : undefined;
  return { candidates, retry: () => retry(value => value + 1) };
}

function AssigneeChoices({ task, choice, setChoice, candidates, retry }: {
  task: Task; choice: string; setChoice: (value: string) => void; candidates: Candidate[] | null | undefined; retry: () => void;
}) {
  return <>
    <label>Assignee<select value={choice} disabled={!candidates} onChange={event => setChoice(event.target.value)}>
      <option value="">Unassigned</option>
      {choice && !candidates?.some(person => String(person.userId) === choice) &&
        <option value={choice} disabled>Previously selected assignee is unavailable</option>}
      {candidates?.map(person => <option key={person.userId} value={person.userId}>{person.userName}</option>)}
    </select></label>
    {candidates === undefined && <p role="status" className="wf-muted">Loading eligible assignees…</p>}
    {candidates === null && <p role="alert" className="wf-muted">Eligible assignees could not be loaded.
      <button type="button" className="tw-inline-action" onClick={retry}>Retry</button></p>}
    <p className="wf-muted">{task.workspaceId != null ? "Assignees must be active workspace members." :
      "Assign the task owner or a collaborator with accepted Edit access. View-only collaborators and pending invitees are ineligible."}</p>
  </>;
}

export function SubTaskAssigneeDialog({ task, item, state, revision, onSave }: {
  task: Task; item: SubTask; state: SubTaskDialogState; revision: string; onSave: (userId: number | null) => void;
}) {
  const [choice, setChoice] = useState(item.assigneeUserId?.toString() ?? "");
  const { candidates, retry } = useEligibleAssignees(task, revision);
  const valid = Boolean(candidates && (!choice || candidates.some(person => String(person.userId) === choice)) &&
    choice !== (item.assigneeUserId?.toString() ?? ""));
  return <TaskDialog title={item.assigneeUserId != null ? "Change subtask assignee" : "Assign subtask"} {...state}>
    <form className="tw-edit-form" onSubmit={event => {
      event.preventDefault();
      if (valid && !state.busy && !state.blocked) onSave(choice ? Number(choice) : null);
    }}>
      <fieldset disabled={state.busy}><AssigneeChoices task={task} choice={choice} setChoice={setChoice}
        candidates={candidates} retry={retry} /></fieldset>
      {item.status === "InProgress" && <p className="wf-muted">Changing or removing the assignee resets this subtask to Not started. Its history stays available.</p>}
      <Actions state={state} label={choice ? "Save assignment" : "Unassign subtask"} allowed={valid} />
    </form>
  </TaskDialog>;
}

export function SubTaskConfirmation({ state, title, explanation, label, danger, onConfirm }: {
  state: SubTaskDialogState; title: string; explanation: string; label: string; danger?: boolean; onConfirm: () => void;
}) {
  return <TaskDialog title={title} {...state}>
    <form onSubmit={event => {
      event.preventDefault();
      if (!state.busy && !state.blocked) onConfirm();
    }}>
      <p>{explanation}</p>
      <Actions state={state} label={label} danger={danger} />
    </form>
  </TaskDialog>;
}
