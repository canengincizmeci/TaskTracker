import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { invitationStatus as invitationState } from "../hooks/useInvitationDeadline";
import type { Participant, OutgoingInvitation } from "../api/taskCollaborationService";
import type { Task } from "../types/task";
import type { TaskPermission } from "../types/taskPermission";
import type { WorkspaceMember } from "../types/workspace";

export type People = { members: WorkspaceMember[]; participants: Participant[]; invitations: OutgoingInvitation[] };
const levels = ["View", "Edit"] as const;
type GrantLevel = typeof levels[number];
export type InvitationDraft = { username: string; permission: GrantLevel; message: string };
const descriptions: Record<GrantLevel, string> = {
  View: "Read the task, join discussion and share reference files.",
  Edit: "Includes View access, editing task details and eligibility for assignment after accepting.",
};

function accessLabel(permission?: TaskPermission | null) {
  return permission === "Manage" ? "Legacy Manage" : permission;
}

export function Avatar({ name }: { name: string }) {
  return <span className="tw-person-avatar" aria-hidden="true">{Array.from(name.trim())[0]?.toUpperCase() || "·"}</span>;
}

function Identity({ name, detail }: { name: string; detail: string }) {
  return <><Avatar name={name} /><span className="tw-person-text"><strong>{name}</strong><small>{detail}</small></span></>;
}

function PersonGroup({ title, children }: { title: string; children: ReactNode }) {
  return <section className="tw-person-group"><h3>{title}</h3>{children}</section>;
}

function InvitationRow({ item, cancel }: { item: OutgoingInvitation; cancel?: () => void }) {
  const time = item.respondedAt ?? item.createdAt;
  return <div className="tw-invitation-row">
    <div className="tw-person-row"><Identity name={item.invitedUserName}
      detail={`${accessLabel(item.permission) ?? "Unavailable"} access · ${invitationState(item)}`} />
      {cancel && <button className="tw-person-link" onClick={cancel} type="button">Cancel invite</button>}
    </div>
    <small className="tw-invitation-time"><time dateTime={time}>{new Date(time).toLocaleString(undefined,
      { dateStyle: "medium", timeStyle: "short" })}</time></small>
    {item.message && <p><strong>Invitation</strong> {item.message}</p>}
    {item.responseMessage && <p><strong>Reply</strong> {item.responseMessage}</p>}
  </div>;
}

export function AssigneePicker({ task, people, query, onQuery, assign, unassign, upgrade, invite, guidedUserId }:
  { task: Task; people: People; query: string; onQuery: (query: string) => void;
    assign: (id: number, name: string) => void; unassign: () => void;
    upgrade: (participant: Participant) => void; invite?: () => void; guidedUserId: number | null }) {
  const search = query.trim().toLocaleLowerCase();
  const matches = (name: string) => name.toLocaleLowerCase().includes(search);
  const workspace = task.workspaceId != null;
  const available = workspace ? people.members : [
    { userId: task.ownerId, userName: task.ownerUserName },
    ...people.participants.filter(p => p.permission === "Edit" || p.permission === "Manage"),
  ];
  const eligible = available.filter(p => p.userId === task.assigneeUserId || matches(p.userName))
    .sort((first, second) => Number(second.userId === task.assigneeUserId) - Number(first.userId === task.assigneeUserId));
  const viewOnly = people.participants.filter(p => p.permission === "View" && matches(p.userName));
  const pending = people.invitations.filter(i => invitationState(i) === "Pending" && matches(i.invitedUserName));
  return <>
    <p className="tw-people-muted">{task.assigneeUserId == null ? `${task.ownerUserName} handles the work until someone is assigned.`
      : `Current assignee: ${task.assigneeUserName ?? task.ownerUserName}.`}</p>
    <label className="tw-people-search"><svg aria-hidden="true" viewBox="0 0 24 24"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 4 4"/></svg>
      <input aria-label="Search people" placeholder="Search people…" type="search" value={query} onChange={e => onQuery(e.target.value)} /></label>
    {eligible.length > 0 && <PersonGroup title={workspace ? "Active workspace members" : "Eligible to assign"}>
      {eligible.map(p => <button key={p.userId} type="button" className="tw-person-row tw-person-choice" data-assignee-id={p.userId}
        disabled={p.userId === task.assigneeUserId} onClick={() => assign(p.userId, p.userName)}>
        <Identity name={p.userName} detail={p.userId === task.ownerId ? "Task owner" : workspace ? "Workspace member" : "Collaborator"} />
        {p.userId === task.assigneeUserId ? <span className="tw-person-badge">Assigned</span>
          : p.userId === guidedUserId && <span className="tw-person-badge">Assign now</span>}
      </button>)}
    </PersonGroup>}
    {viewOnly.length > 0 && <PersonGroup title="Needs Edit access">
      {viewOnly.map(p => <div key={p.userId} className="tw-person-row">
        <Identity name={p.userName} detail="View access" />
        <button className="tw-person-link" type="button" aria-label={`Grant Edit to ${p.userName}`} onClick={() => upgrade(p)}>Grant Edit</button>
      </div>)}
    </PersonGroup>}
    {pending.length > 0 && <PersonGroup title="Awaiting acceptance">
      {pending.map(i => <div key={i.id} className="tw-person-row"><Identity name={i.invitedUserName}
        detail={`Not assignable until accepted · ${accessLabel(i.permission) ?? "unavailable"} access`} /></div>)}
    </PersonGroup>}
    {!eligible.length && !viewOnly.length && !pending.length && <p className="tw-people-muted">No matching people.</p>}
    {(invite || task.assigneeUserId != null) && <div className="tw-people-footer">
      {invite && <button type="button" className="tw-person-link" onClick={invite}>Invite someone new</button>}
      {task.assigneeUserId != null && <button type="button" className="tw-person-link tw-person-link--muted" onClick={unassign}>Clear assignee</button>}
    </div>}
  </>;
}

export function ShareRoster({ task, people, invite, manage, cancel }: {
  task: Task; people: People; invite?: () => void; manage?: (person: Participant) => void;
  cancel: (invitation: OutgoingInvitation) => void;
}) {
  const workspace = task.workspaceId != null;
  const pending = people.invitations.filter(i => invitationState(i) === "Pending");
  const history = people.invitations.filter(i => invitationState(i) !== "Pending");
  const roles = (userId: number, detail: string) => [detail, userId === task.assigneeUserId && "Assignee",
    userId === task.ownerId && task.assigneeUserId == null && "Handles the work"].filter(Boolean).join(" · ");
  return <>
    <div className="tw-people-intro">
      <p>{workspace ? "Active workspace members have access to this task." :
        "People with access can follow the work and join the discussion."}</p>
      {invite && <button className="tw-person-primary" type="button" onClick={invite}>Invite someone</button>}
    </div>
    <PersonGroup title="People with access">
      {(!workspace || people.members.some(member => member.userId === task.ownerId)) &&
        <div className="tw-person-row"><Identity name={task.ownerUserName} detail={roles(task.ownerId, "Task owner")} /></div>}
      {workspace ? people.members.filter(m => m.userId !== task.ownerId).map(m =>
        <div className="tw-person-row" key={m.userId}><Identity name={m.userName} detail={roles(m.userId, `Workspace ${m.role.toLowerCase()}`)} /></div>
      ) : people.participants.map(p =>
        <div className="tw-person-row" key={p.userId}><Identity name={p.userName}
          detail={roles(p.userId, p.permission ? `${accessLabel(p.permission)} access` : "Access unavailable")} />
          {manage && <button type="button" className="tw-person-link"
            aria-label={`Manage access for ${p.userName}`} onClick={() => manage(p)}>Manage</button>}
        </div>)}
    </PersonGroup>
    {workspace && <div className="tw-people-footer"><Link className="tw-person-link" to={`/workspaces/${task.workspaceId}`}>Open workspace members</Link></div>}
    {pending.length > 0 && <PersonGroup title="Pending invitations">{pending.map(i =>
      <InvitationRow key={i.id} item={i} cancel={i.canCancel ? () => cancel(i) : undefined} />)}</PersonGroup>}
    {history.length > 0 && <details className="tw-invite-history"><summary>Invitation history <span>{history.length}</span></summary>
      {history.map(i => <InvitationRow key={i.id} item={i} />)}</details>}
  </>;
}

export function InviteForm({ draft, setDraft, send }: { draft: InvitationDraft;
  setDraft: (draft: InvitationDraft) => void; send: () => void }) {
  return <form className="tw-people-form" onSubmit={e => { e.preventDefault(); if (draft.username.trim()) send(); }}>
    <p className="tw-people-muted">Access starts when they accept. Choose an assignee separately.</p>
    <label>TaskTracker username<input type="text" value={draft.username} required maxLength={50} autoComplete="off"
      placeholder="Enter exact username" onChange={e => setDraft({ ...draft, username: e.target.value })} /></label>
    <fieldset className="tw-person-levels"><legend>Access level</legend><div>{levels.map(level =>
      <label key={level}><input type="radio" name="invite-level" checked={level === draft.permission}
        onChange={() => setDraft({ ...draft, permission: level })} /><span>{level}</span></label>)}</div>
      <p>{descriptions[draft.permission]}</p></fieldset>
    <label>Message <small>Optional</small><textarea rows={3} maxLength={1000} value={draft.message}
      placeholder="What would you like help with?" onChange={e => setDraft({ ...draft, message: e.target.value })} /></label>
    <button type="submit" className="tw-person-primary" disabled={!draft.username.trim()}>Send invitation</button>
  </form>;
}

export function EditAccess({ task, person, change, remove }: { task: Task; person: Participant;
  change: (level: TaskPermission) => void; remove: () => void }) {
  const reviewing = task.status === "InReview";
  return <><div className="tw-person-row"><Identity name={person.userName} detail={person.isAssignee ? "Assignee" : "Collaborator"} /></div>
    {person.permission === "Manage" && <p className="tw-people-muted">Legacy Manage is saved on this task and works like Edit. Access changes remain owner-only.</p>}
    <PersonGroup title="Access level">{levels.map(level => <button key={level} className="tw-person-row tw-person-choice"
      type="button" disabled={person.permission === level || (reviewing && person.isAssignee && level === "View")}
      onClick={() => change(level)}><span className="tw-person-text"><strong>{level}</strong><small>{descriptions[level]}</small></span>
      {person.permission === level && <span className="tw-person-badge">Current</span>}</button>)}</PersonGroup>
    {reviewing ? <p className="tw-people-muted">You can remove collaborators after the review.</p> :
      <div className="tw-people-footer"><button type="button" className="tw-person-link tw-person-link--danger" onClick={remove}>Remove access</button></div>}
  </>;
}
