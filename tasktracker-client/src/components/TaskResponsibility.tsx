import { useEffect, useRef, useState, type RefObject } from "react";
import { createPortal } from "react-dom";
import { useLocation, useSearchParams } from "react-router-dom";
import { isAxiosError } from "axios";
import { useInvitationDeadline } from "../hooks/useInvitationDeadline";
import { useModalFocus } from "../hooks/useModalFocus";
import { assignTask, taskAction } from "../api/taskService";
import { cancelInvitation, getOutgoingInvitations, getParticipants, removeParticipant, updateParticipantPermission,
  type Participant } from "../api/taskCollaborationService";
import { inviteToTask } from "../api/taskInvitationService";
import { getWorkspaceMembers } from "../api/workspaceService";
import { errorMessage } from "../api/errorMessage";
import { useAuth } from "../context/AuthContext";
import { useTaskConversation } from "../hooks/useTaskConversation";
import type { Task } from "../types/task";
import { AssigneePicker, Avatar, EditAccess, InviteForm, ShareRoster, type InvitationDraft, type People } from "./TaskPeopleViews";
import "../styles/task-people.css";

type Mode = "assignee" | "share" | "invite";
type Props = { task: Task; onChanged: () => Promise<number | undefined>; disabled?: boolean };
type Command = { run: () => Promise<unknown>; message: string; after?: () => void };
type Confirmation = { version: number; rosterRevision: number; label: string; explanation: string; command: Command };
const emptyDraft: InvitationDraft = { username: "", permission: "View", message: "" };

function PeopleDialog({ task, onChanged, disabled, mode, canInvite, canManageAccess, navigate, draft, setDraft, opener, fallback }: Props & {
  mode: Mode; canInvite: boolean; canManageAccess: boolean; navigate: (mode: Mode | null) => void;
  draft: InvitationDraft; setDraft: (draft: InvitationDraft) => void;
  opener: RefObject<HTMLElement | null>;
  fallback: RefObject<HTMLElement | null>;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const lock = useRef(false);
  const done = useRef<HTMLButtonElement>(null);
  const focusDone = useRef(false);
  const mounted = useRef(false);
  const [busy, setBusy] = useState(false);
  const [query, setQuery] = useState("");
  const [editing, setEditing] = useState<number | null>(null);
  const [confirm, setConfirm] = useState<Confirmation | null>(null);
  const [notice, setNotice] = useState("");
  const [failure, setFailure] = useState("");
  const [guidedUserId, setGuidedUserId] = useState<number | null>(null);
  const [needsRefresh, setNeedsRefresh] = useState(false);
  const [epoch, setEpoch] = useState(0);
  const [snapshot, setSnapshot] = useState<{ key: string; data?: People; error?: string }>();
  const { rosterRevision, liveError } = useTaskConversation();
  const [inviteReturn, setInviteReturn] = useState<"share" | "assignee">("share");
  const key = [task.id, task.version, task.workspaceId, canManageAccess, epoch, rosterRevision].join("/");
  const people = snapshot?.data;
  const stale = mode !== "invite" && snapshot?.key !== key;
  useInvitationDeadline(people?.invitations ?? [], () => setEpoch(current => current + 1));
  const loadError = snapshot?.key === key ? snapshot.error : undefined;
  const participant = people?.participants.find(p => p.userId === editing);
  const confirmation = confirm?.version === task.version && confirm.rosterRevision === rosterRevision ? confirm : null;
  const editingAccess = mode === "share" && editing != null && canManageAccess;
  const focusState = confirmation ? "confirm" : editingAccess ? "edit" : mode;
  const ready = mode === "invite" || Boolean(people || loadError);
  const close = () => { if (!lock.current) navigate(null); };

  useModalFocus(dialog, opener, fallback);
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  useEffect(() => {
    if (!ready) return;
    const target = dialog.current?.querySelector<HTMLElement>(
      focusState === "assignee" ? 'input[type="search"]' : focusState === "invite" ? 'input[type="text"]' : "h2");
    target?.focus({ preventScroll: true });
  }, [focusState, ready]);

  useEffect(() => {
    if (stale || mode !== "assignee" || guidedUserId === null || confirmation) return;
    dialog.current?.querySelector<HTMLElement>(`button[data-assignee-id="${guidedUserId}"]:not(:disabled)`)?.focus({ preventScroll: true });
  }, [confirmation, guidedUserId, mode, stale]);
  useEffect(() => {
    if (!busy && focusDone.current) {
      focusDone.current = false;
      done.current?.focus({ preventScroll: true });
    }
  }, [busy]);

  useEffect(() => {
    if (mode === "invite") return;
    let active = true;
    const request = task.workspaceId != null
      ? getWorkspaceMembers(task.workspaceId).then(members => ({ members, participants: [], invitations: [] }))
      : Promise.all([getParticipants(task.id), canManageAccess ? getOutgoingInvitations(task.id) : Promise.resolve([])])
        .then(([participants, invitations]) => ({ participants, invitations, members: [] }));
    request.then(data => { if (active) setSnapshot({ key, data }); })
      .catch((reason: unknown) => { if (active) setSnapshot(previous => ({
        key, data: previous?.data, error: errorMessage(reason, "Could not load people."),
      })); });
    return () => { active = false; };
  }, [canManageAccess, key, mode, task.id, task.workspaceId]);

  const refresh = async () => {
    try {
      const version = await onChanged();
      if (version === undefined) {
        if (mounted.current) setNeedsRefresh(true);
        return false;
      }
      if (mounted.current) {
        if (version === task.version) setEpoch(current => current + 1);
        setNeedsRefresh(false);
      }
      return true;
    } catch {
      if (mounted.current) setNeedsRefresh(true);
      return false;
    }
  };
  const execute = async (command: Command) => {
    if (lock.current || disabled || needsRefresh || stale || (loadError && mode !== "invite")) return;
    lock.current = true; setBusy(true); setNotice(""); setFailure(""); setConfirm(null);
    try {
      await command.run();
      const synced = await refresh();
      if (!mounted.current) return;
      if (synced) { setNotice(command.message); command.after?.(); }
      else setNotice(command.message);
    } catch (reason: unknown) {
      if (!mounted.current) return;
      const status = isAxiosError(reason) ? reason.response?.status : undefined;
      const message = errorMessage(reason, "The change could not be saved.");
      setFailure(status === 409 ? `${message} Check the updated people list and choose again.` : message);
      if (status === 400 || status === 403 || status === 409) await refresh();
    } finally { lock.current = false; if (mounted.current) setBusy(false); }
  };
  const ask = (label: string, explanation: string, command: Command) => {
    setNotice(""); setFailure(""); setConfirm({ label, explanation, command, version: task.version, rosterRevision });
  };
  const switchMode = (next: Mode) => {
    setConfirm(null); setEditing(null); setNotice(""); setFailure(""); navigate(next);
  };
  const resetWarning = task.status === "InProgress" ? " Work returns to Not started. Previous revisions stay in Activity." : "";
  const assign = (userId: number | null, name?: string) => {
    setGuidedUserId(null);
    const message = userId == null ? "Assignee cleared." : `Assigned to ${name}.`;
    const command: Command = {
      run: () => userId == null ? taskAction(task.id, "unassign", task.version) : assignTask(task.id, userId, task.version),
      message,
      after: () => { focusDone.current = true; },
    };
    if (resetWarning || userId == null) ask(userId == null ? "Clear assignee" : "Change assignee",
      (userId == null ? "The owner will handle this task." : `${name} will be responsible for this work.`) + resetWarning, command);
    else void execute(command);
  };
  const upgrade = (p: Participant) => ask("Grant Edit", `${p.userName} will be able to edit task details. This does not assign the task.`,
    { run: () => updateParticipantPermission(task.id, p.userId, "Edit", task.version),
      message: `Edit granted. ${p.userName} is ready to assign.`, after: () => setGuidedUserId(p.userId) });
  const invite = (from: "share" | "assignee") => {
    setInviteReturn(from);
    switchMode("invite");
  };
  const title = confirmation?.label ?? (editingAccess ? "Manage access" : mode === "assignee" ? "Choose assignee" :
    mode === "invite" ? "Invite a collaborator" : "People & access");

  return createPortal(<dialog ref={dialog} aria-labelledby="tw-people-title" className="tw-people-dialog"
    onCancel={e => { e.preventDefault(); close(); }} onClick={e => {
      if (e.target === dialog.current) {
        const r = dialog.current.getBoundingClientRect();
        if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom) close();
      }
    }}>
    <header className="tw-dialog-head"><div><small title={task.title}>{task.title}</small>
      <h2 id="tw-people-title" tabIndex={-1}>{title}</h2></div>
      <button className="tw-dialog-close" type="button" aria-label="Close people dialog" disabled={busy} onClick={close}>
        <svg aria-hidden="true" viewBox="0 0 24 24"><path d="m6 6 12 12M18 6 6 18"/></svg>
      </button></header>
    <div className="tw-people-body">
      {notice && <p role="status" className="tw-people-notice">{notice}</p>}
      {failure && <p role="alert" className="tw-people-error">{failure}</p>}
      {confirm && !confirmation && <p role="alert" className="tw-people-error">The task or people list changed. Check the current selection before confirming again.</p>}
      {liveError && mode !== "invite" && <p className="tw-people-muted">{liveError}{" "}
        <button type="button" className="tw-person-link" disabled={busy} onClick={() => void refresh()}>Refresh people</button></p>}
      {needsRefresh && <div className="tw-people-error" role="alert">Task details could not be refreshed. <button className="tw-person-link"
        type="button" disabled={busy} onClick={() => void refresh()}>Refresh details</button></div>}
      {busy ? <p role="status" className="tw-people-muted">Saving change…</p> :
        stale && people && <p role="status" className="tw-people-muted">Updating people…</p>}
      {loadError && mode !== "invite" && <div role="alert" className="tw-people-error">{loadError} <button type="button"
        className="tw-person-link" onClick={() => setEpoch(n => n + 1)}>Try again</button></div>}
      <fieldset className="tw-people-controls" aria-label={title} disabled={busy || disabled || needsRefresh || stale || (!!loadError && mode !== "invite")}>
      {confirmation ? <div className="tw-people-confirm"><p>{confirmation.explanation}</p>
        <div className="tw-confirm-actions"><button type="button" className="tw-person-link" onClick={() => setConfirm(null)}>Go back</button>
          <button type="button" className="tw-person-primary" onClick={() => void execute(confirmation.command)}>{confirmation.label}</button>
        </div></div> : mode === "invite" ? <>
        <button className="tw-person-link tw-person-back" type="button" onClick={() => switchMode(inviteReturn)}>
          {inviteReturn === "assignee" ? "← Assignee" : "← People & access"}</button>
        <InviteForm draft={draft} setDraft={next => { setDraft(next); setFailure(""); setNotice(""); }} send={() => {
          const name = draft.username.trim();
          void execute({ run: async () => { await inviteToTask(task.id, name, draft.permission, draft.message); setDraft(emptyDraft); },
            message: `Invitation sent to ${name}. Choose them after they accept.`, after: () => {
              if (inviteReturn === "assignee") setQuery(name);
              navigate(inviteReturn);
            } });
        }} /></> : !people ? !loadError && <p role="status" className="tw-people-muted">Loading people…</p>
        : mode === "assignee" ? <AssigneePicker task={task} people={people} query={query} onQuery={setQuery}
          assign={assign} unassign={() => assign(null)} upgrade={upgrade} guidedUserId={guidedUserId}
          invite={canInvite ? () => invite("assignee") : undefined} />
        : editingAccess && participant ? <>
          <button className="tw-person-link tw-person-back" type="button" onClick={() => { setEditing(null); setFailure(""); setNotice(""); }}>← People & access</button>
          <EditAccess task={task} person={participant} change={level => {
            const command: Command = { run: () => updateParticipantPermission(task.id, participant.userId, level, task.version),
              message: `${participant.userName} now has ${level} access.`, after: () => setEditing(null) };
            if (participant.isAssignee && level === "View") ask("Change to View",
              `This clears ${participant.userName} as assignee.${resetWarning}`, command);
            else void execute(command);
          }} remove={() => ask("Remove access", `${participant.userName} will lose collaborator access.` +
            (participant.isAssignee ? ` This clears their assignment.${resetWarning}` : ""), {
            run: () => removeParticipant(task.id, participant.userId, task.version), message: "Collaborator access removed.", after: () => setEditing(null),
          })} /></> : <ShareRoster task={task} people={people} invite={canInvite ? () => invite("share") : undefined}
          manage={canManageAccess ? p => { setNotice(""); setFailure(""); setEditing(p.userId); } : undefined} cancel={i => ask("Cancel invitation",
            `${i.invitedUserName} will no longer be able to accept this invitation.`, {
              run: () => cancelInvitation(i.id, task.version), message: "Invitation cancelled.",
            })} />}
      </fieldset>
      {mode === "assignee" && <div className="tw-people-footer"><button ref={done} type="button" className="tw-person-link"
        disabled={busy} onClick={close}>Done</button></div>}
    </div>
  </dialog>, document.body);
}

export default function TaskResponsibility({ task, onChanged, disabled = false }: Props) {
  const [params, setParams] = useSearchParams();
  const { hash } = useLocation();
  const { user } = useAuth();
  const trigger = useRef<HTMLElement>(null);
  const shareTrigger = useRef<HTMLButtonElement>(null);
  const opener = useRef<HTMLElement>(null);
  const [draft, setDraft] = useState<InvitationDraft>(emptyDraft);
  const requested = params.get("people");
  const canInspect = task.canViewParticipants === true;
  const manage = task.isOwner === true && task.workspaceId == null && user?.role === "User";
  const canInvite = manage && task.canShare === true && task.activity && !["Completed", "Cancelled"].includes(task.status);
  const canAssign = user?.role === "User" && task.canManageResponsibility === true && ["Pending", "InProgress"].includes(task.status);
  const mode = !canInspect ? null : requested === "assignee" && canAssign ? "assignee" :
    requested === "invite" && canInvite ? "invite" : ["invite", "share", "assignee"].includes(requested ?? "") ? "share" : null;
  const assigneeName = task.assigneeUserName ?? task.ownerUserName;
  const responsibility = task.assigneeUserId == null ? "Owner handles work · not separately assigned"
    : task.assigneeUserId === task.ownerId ? "Owner explicitly assigned" : null;
  const navigate = (next: Mode | null) => {
    setParams(previous => {
      const updated = new URLSearchParams(previous);
      if (next) updated.set("people", next); else updated.delete("people");
      return updated;
    }, { replace: true, preventScrollReset: true });
  };
  useEffect(() => { if (hash === "#task-responsibility") trigger.current?.focus(); }, [hash]);
  const property = <><span className="tw-assignee-label">Assignee</span><Avatar name={assigneeName} />
    <strong title={assigneeName}>{assigneeName}</strong>{responsibility && <small>{responsibility}</small>}
    {task.isOwner && task.status === "InReview" && <small>Locked during review</small>}</>;
  return <>
    {canAssign ? <button type="button" ref={node => { trigger.current = node; }} className="tw-assignee" id="task-responsibility"
      aria-haspopup="dialog" aria-expanded={mode === "assignee"} disabled={disabled}
      aria-label={`Change assignee. ${assigneeName}${responsibility ? `: ${responsibility}` : ""}`}
      onClick={event => { opener.current = event.currentTarget; navigate("assignee"); }}>{property}<svg aria-hidden="true" viewBox="0 0 24 24"><path d="m7 10 5 5 5-5"/></svg></button>
      : <span ref={node => { trigger.current = node; }} className="tw-assignee" id="task-responsibility" tabIndex={-1}>{property}</span>}
    {canInspect && <button ref={shareTrigger} type="button" className="tw-people-trigger" disabled={disabled} aria-haspopup="dialog"
      aria-expanded={mode === "share" || mode === "invite"} onClick={event => { opener.current = event.currentTarget; navigate("share"); }}>
      <svg aria-hidden="true" viewBox="0 0 24 24"><circle cx="9" cy="8" r="3"/><path d="M3 20v-2a6 6 0 0 1 12 0v2M18 8v6m-3-3h6"/></svg>
      {canInvite ? "Share" : "People"}</button>}
    {mode && <PeopleDialog key={task.id} task={task} mode={mode} canInvite={canInvite} canManageAccess={manage}
      draft={draft} setDraft={setDraft} navigate={navigate} onChanged={onChanged} disabled={disabled} opener={opener} fallback={shareTrigger} />}
  </>;
}
