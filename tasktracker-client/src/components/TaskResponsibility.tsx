import { useEffect, useState } from "react";
import { assignTask, taskAction } from "../api/taskService";
import {
  cancelInvitation, getOutgoingInvitations, getParticipants, removeParticipant,
  updateParticipantPermission, type OutgoingInvitation, type Participant,
} from "../api/taskCollaborationService";
import { errorMessage } from "../api/errorMessage";
import { getWorkspaceMembers } from "../api/workspaceService";
import type { Task } from "../types/task";
import type { TaskPermission } from "../types/taskPermission";
import type { WorkspaceMember } from "../types/workspace";

export default function TaskResponsibility({ task, onChanged }: {
  task: Task;
  onChanged: () => Promise<void>;
}) {
  const [participants, setParticipants] = useState<Participant[]>([]);
  const [workspaceMembers, setWorkspaceMembers] = useState<WorkspaceMember[]>([]);
  const [invitations, setInvitations] = useState<OutgoingInvitation[]>([]);
  const [selected, setSelected] = useState(String(task.assigneeUserId ?? ""));
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState("");

  useEffect(() => {
    let active = true;
    const details = task.workspaceId
      ? getWorkspaceMembers(task.workspaceId).then((members) => ({ members, participants: [], invitations: [] }))
      : Promise.all([
          getParticipants(task.id),
          task.isOwner ? getOutgoingInvitations(task.id) : Promise.resolve([]),
        ]).then(([participants, invitations]) => ({ members: [], participants, invitations }));

    details.then(({ members, participants: accepted, invitations: pending }) => {
      if (active) {
        setWorkspaceMembers(members);
        setParticipants(accepted);
        setInvitations(pending);
        setFailure("");
      }
    }).catch((reason: unknown) => {
      if (active) setFailure(errorMessage(reason, "Could not load responsibility details."));
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [task.id, task.isOwner, task.version, task.workspaceId]);

  const mutate = async (operation: () => Promise<unknown>) => {
    if (busy) return;
    setBusy(true); setFailure("");
    try { await operation(); await onChanged(); }
    catch (reason: unknown) { setFailure(errorMessage(reason, "The change could not be saved.")); }
    finally { setBusy(false); }
  };

  const eligible = participants.filter((participant) =>
    participant.permission === "Edit" || participant.permission === "Manage");
  const isWorkspaceTask = task.workspaceId != null;
  const canManageResponsibility = isWorkspaceTask
    ? task.canManageResponsibility === true
    : task.isOwner === true;

  return <section className="task-detail-section" aria-label="Responsibility and participants">
    <div className="task-section-header">
      <div><p className="eyebrow">RESPONSIBILITY</p><h2>Owner and assignee</h2></div>
    </div>
    <p><strong>Owner:</strong> {task.ownerUserName}</p>
    <p><strong>Assignee:</strong> {task.assigneeUserName ?? "Unassigned"}</p>
    {!isWorkspaceTask && !task.isOwner && task.currentUserPermission && <p><strong>Your access:</strong> {task.currentUserPermission}</p>}
    {failure && <p role="alert" className="error-message">{failure}</p>}
    {loading && <p>Loading participants...</p>}

    {canManageResponsibility && task.status !== "InReview" && !loading && <div className="task-detail-actions">
      <select aria-label="Assignee" value={selected} disabled={busy}
        onChange={(event) => setSelected(event.target.value)}>
        <option value="">Unassigned</option>
        {isWorkspaceTask
          ? workspaceMembers.map((member) => <option key={member.userId} value={member.userId}>
              {member.userName}{member.userId === task.ownerId ? " (task owner)" : ""}
            </option>)
          : <>
              <option value={task.ownerId}>{task.ownerUserName} (owner)</option>
              {eligible.map((participant) => <option key={participant.userId} value={participant.userId}>
                {participant.userName}
              </option>)}
            </>}
      </select>
      <button type="button" className="primary-button" disabled={busy || !selected || Number(selected) === task.assigneeUserId}
        onClick={() => void mutate(() => assignTask(task.id, Number(selected), task.version))}>
        {task.assigneeUserId ? "Reassign" : "Assign"}
      </button>
      {task.assigneeUserId && <button type="button" className="secondary-button" disabled={busy}
        onClick={() => void mutate(() => taskAction(task.id, "unassign", task.version))}>Unassign</button>}
    </div>}

    {isWorkspaceTask ? <>
      <h3>Active workspace members</h3>
      {!loading && workspaceMembers.length === 0 && <p>No active workspace members are available.</p>}
      <ul className="activity-timeline">{workspaceMembers.map((member) => <li className="timeline-item" key={member.userId}>
        <strong>{member.userName}{member.userId === task.assigneeUserId ? " · Assignee" : ""}</strong>
        <span>Workspace role: {member.role}</span>
      </li>)}</ul>
    </> : <>
      <h3>Accepted collaborators</h3>
      {!loading && participants.length === 0 && <p>No accepted collaborators yet.</p>}
      <ul className="activity-timeline">{participants.map((participant) => <li className="timeline-item" key={participant.userId}>
        <strong>{participant.userName}{participant.isAssignee ? " · Assignee" : ""}</strong>
        <span>Access: {participant.permission ?? "Invalid"}</span>
        {task.isOwner && task.status !== "InReview" && <div className="task-detail-actions">
          <select aria-label={`Permission for ${participant.userName}`} value={participant.permission ?? "View"} disabled={busy}
            onChange={(event) => void mutate(() => updateParticipantPermission(task.id, participant.userId,
              event.target.value as TaskPermission, task.version))}>
            <option value="View">View</option><option value="Edit">Edit</option><option value="Manage">Manage</option>
          </select>
          <button type="button" className="danger-button" disabled={busy}
            onClick={() => window.confirm(`Remove ${participant.userName} from this task?`) &&
              void mutate(() => removeParticipant(task.id, participant.userId, task.version))}>Remove participant</button>
        </div>}
      </li>)}</ul>

      {task.isOwner && <>
        <h3>Pending invitations</h3>
        {!loading && invitations.length === 0 && <p>No pending outgoing invitations.</p>}
        <ul className="activity-timeline">{invitations.map((invitation) => <li className="timeline-item" key={invitation.id}>
          <strong>{invitation.invitedUserName}</strong><span>{invitation.permission} · {invitation.status}</span>
          {invitation.canCancel && <button type="button" className="secondary-button" disabled={busy}
            onClick={() => void mutate(() => cancelInvitation(invitation.id, task.version))}>Cancel invitation</button>}
        </li>)}</ul>
      </>}
    </>}
  </section>;
}
