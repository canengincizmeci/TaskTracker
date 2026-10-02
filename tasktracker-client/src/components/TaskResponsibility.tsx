import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
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
  const [open, setOpen] = useState(false);
  const [participants, setParticipants] = useState<Participant[]>([]);
  const [workspaceMembers, setWorkspaceMembers] = useState<WorkspaceMember[]>([]);
  const [invitations, setInvitations] = useState<OutgoingInvitation[]>([]);
  const [selected, setSelected] = useState(String(task.assigneeUserId ?? ""));
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState("");

  const isWorkspaceTask = task.workspaceId != null;
  const terminalOrReview = task.status === "Completed" || task.status === "Cancelled" || task.status === "InReview";
  const canManageAssignment = !terminalOrReview && (isWorkspaceTask
    ? task.canManageResponsibility === true
    : task.isOwner === true);
  const canInspectAccess = !isWorkspaceTask && task.canViewParticipants === true;
  const canOpen = canManageAssignment || canInspectAccess;

  useEffect(() => {
    if (!open || loaded) return;
    let active = true;
    const details = isWorkspaceTask
      ? getWorkspaceMembers(task.workspaceId!).then((members) => ({ members, participants: [], invitations: [] }))
      : Promise.all([
          getParticipants(task.id),
          task.isOwner ? getOutgoingInvitations(task.id) : Promise.resolve([]),
        ]).then(([accepted, pending]) => ({ members: [], participants: accepted, invitations: pending }));

    details.then(({ members, participants: accepted, invitations: pending }) => {
      if (active) {
        setWorkspaceMembers(members); setParticipants(accepted); setInvitations(pending);
        setFailure(""); setLoaded(true);
      }
    }).catch((reason: unknown) => {
      if (active) setFailure(errorMessage(reason, "Assignment details could not be loaded."));
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [isWorkspaceTask, loaded, open, task.id, task.isOwner, task.workspaceId]);

  const mutate = async (operation: () => Promise<unknown>) => {
    if (busy) return;
    setBusy(true); setFailure("");
    try {
      await operation(); await onChanged(); setLoaded(false);
    } catch (reason: unknown) { setFailure(errorMessage(reason, "The change could not be saved.")); }
    finally { setBusy(false); }
  };

  const eligible = participants.filter((participant) =>
    participant.permission === "Edit" || participant.permission === "Manage");

  return <section className="task-people-card" aria-labelledby="task-assignment-title">
    <div className="task-section-header"><div><p className="eyebrow">PEOPLE</p>
      <h2 id="task-assignment-title">Assignment</h2></div></div>
    <div className="task-people-summary">
      <div><span>Owner</span><strong>{task.ownerUserName}</strong></div>
      <div><span>Assigned to</span><strong>{task.assigneeUserName ?? "Unassigned"}</strong></div>
    </div>

    {canOpen && <button type="button" className="task-disclosure-button" aria-expanded={open}
      aria-controls="task-assignment-management" onClick={() => {
        if (!open && !loaded) setLoading(true);
        setOpen((current) => !current);
      }}>
      {open ? "Hide assignment and access" : canManageAssignment ?
        (isWorkspaceTask ? "Manage assignment" : "Manage assignment and access") : "View people with access"}
    </button>}

    {open && <div id="task-assignment-management" className="task-assignment-management">
      {failure && <p role="alert" className="error-message">{failure}</p>}
      {loading && <p role="status">Loading assignment details...</p>}

      {canManageAssignment && !loading && <div className="task-assignment-controls">
        <label htmlFor="task-assignee">Assigned to</label>
        <div className="task-detail-actions">
          <select id="task-assignee" value={selected} disabled={busy}
            onChange={(event) => setSelected(event.target.value)}>
            <option value="">Unassigned</option>
            {isWorkspaceTask
              ? workspaceMembers.map((member) => <option key={member.userId} value={member.userId}>
                  {member.userName}{member.userId === task.ownerId ? " (task owner)" : ""}
                </option>)
              : <><option value={task.ownerId}>{task.ownerUserName} (owner)</option>
                  {eligible.map((participant) => <option key={participant.userId} value={participant.userId}>
                    {participant.userName}</option>)}</>}
          </select>
          <button type="button" className="secondary-button"
            disabled={busy || !selected || Number(selected) === task.assigneeUserId}
            onClick={() => void mutate(() => assignTask(task.id, Number(selected), task.version))}>
            {task.assigneeUserId ? "Reassign" : "Assign"}
          </button>
          {task.assigneeUserId && <button type="button" className="secondary-button" disabled={busy}
            onClick={() => void mutate(() => taskAction(task.id, "unassign", task.version))}>Unassign</button>}
        </div>
        {task.status === "InProgress" && <p className="task-control-note">
          Reassigning or unassigning returns this task to Not started.
        </p>}
      </div>}

      {!isWorkspaceTask && !loading && <section className="task-access-management" aria-labelledby="task-access-title">
        <div className="task-access-heading"><h3 id="task-access-title">People with access</h3>
          {task.canShare && <Link className="secondary-button" to={`/tasks/task-share/${task.id}`}>Invite collaborator</Link>}
        </div>
        {participants.length === 0 && <p>No collaborators have access yet.</p>}
        <ul className="activity-timeline">{participants.map((participant) => <li className="timeline-item" key={participant.userId}>
          <strong>{participant.userName}{participant.isAssignee ? " · Assigned" : ""}</strong>
          <span>Access level: {participant.permission ?? "Unavailable"}</span>
          {task.isOwner && task.status !== "InReview" && <div className="task-detail-actions">
            <select aria-label={`Access level for ${participant.userName}`} value={participant.permission ?? "View"} disabled={busy}
              onChange={(event) => void mutate(() => updateParticipantPermission(task.id, participant.userId,
                event.target.value as TaskPermission, task.version))}>
              <option value="View">Can view</option><option value="Edit">Can edit</option><option value="Manage">Can manage access</option>
            </select>
            <button type="button" className="danger-button" disabled={busy}
              onClick={() => window.confirm(`Remove ${participant.userName} from this task?`) &&
                void mutate(() => removeParticipant(task.id, participant.userId, task.version))}>Remove access</button>
          </div>}
        </li>)}</ul>

        {task.isOwner && <><h3>Pending invitations</h3>
          {invitations.length === 0 && <p>No pending invitations.</p>}
          <ul className="activity-timeline">{invitations.map((invitation) => <li className="timeline-item" key={invitation.id}>
            <strong>{invitation.invitedUserName}</strong><span>Access level: {invitation.permission} · {invitation.status}</span>
            {invitation.canCancel && <button type="button" className="secondary-button" disabled={busy}
              onClick={() => void mutate(() => cancelInvitation(invitation.id, task.version))}>Cancel invitation</button>}
          </li>)}</ul></>}
      </section>}
    </div>}
  </section>;
}
