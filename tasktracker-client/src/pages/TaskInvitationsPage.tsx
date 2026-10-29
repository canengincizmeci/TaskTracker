import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  acceptTaskInvitation,
  getMyPendingInvitations,
  rejectTaskInvitation,
} from "../api/taskInvitationService";
import { errorMessage as getErrorMessage } from "../api/errorMessage";
import { useAuth } from "../context/AuthContext";
import { permissionText } from "../types/taskPermission";
import type { TaskInvitation } from "../types/taskInvitation";
import InboxLocalNav from "../components/InboxLocalNav";

function formatDate(value: string) {
  return new Date(value).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

function invitationStatus(invitation: TaskInvitation) {
  return invitation.status.toLowerCase() === "pending" && invitation.expiresAt &&
    new Date(invitation.expiresAt).getTime() <= Date.now() ? "Expired" : invitation.status;
}

function isActionableInvitation(invitation: TaskInvitation) {
  return invitationStatus(invitation).toLowerCase() === "pending" &&
    (invitation.canAccept || invitation.canReject);
}

function TaskInvitationsPage() {
  const navigate = useNavigate();
  const { user } = useAuth();
  const [invitations, setInvitations] = useState<TaskInvitation[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState("");

  useEffect(() => {
    let cancelled = false;
    getMyPendingInvitations().then((data) => {
      if (!cancelled) setInvitations(data);
    }).catch((error: unknown) => {
      if (!cancelled) setErrorMessage(getErrorMessage(error, "Could not load invitations."));
    }).finally(() => {
      if (!cancelled) setLoading(false);
    });
    return () => { cancelled = true; };
  }, []);

  const activeRequests = useRef(new Set<number>());
  const [busyIds, setBusyIds] = useState<Set<number>>(new Set());

  const respond = async (id: number, accept: boolean) => {
    if (activeRequests.current.has(id)) return;
    activeRequests.current.add(id);
    setBusyIds(new Set(activeRequests.current));
    setErrorMessage("");
    try {
      await (accept ? acceptTaskInvitation(id) : rejectTaskInvitation(id));
      const acceptedTask = invitations.find((item) => item.id === id)?.taskRequestId;
      setInvitations((current) => current.filter((item) => item.id !== id));
      if (accept && acceptedTask) navigate(`/tasks/task-detail/${acceptedTask}`);
    } catch (error: unknown) {
      setErrorMessage(getErrorMessage(error, "Could not respond to invitation."));
    } finally {
      activeRequests.current.delete(id);
      setBusyIds(new Set(activeRequests.current));
    }
  };

  const pendingInvitations = invitations.filter(isActionableInvitation);
  const canUseInvitationActions = user?.role === "User";

  return (
    <main className="page public-page inbox-page">
      <div className="inbox-shell">
        <header className="inbox-header">
          <div><p className="eyebrow">INBOX</p><h1>Task invitations</h1>
            <p>Review requests to collaborate on tasks shared with you.</p></div>
          {!loading && !errorMessage && <span className="inbox-count">{pendingInvitations.length} pending</span>}
        </header>

        <InboxLocalNav />

        {errorMessage && <p role="alert" className="inbox-alert">{errorMessage}</p>}
        {loading ? <div className="inbox-state" role="status">Loading invitations...</div> :
          errorMessage ? null : invitations.length === 0 ? (
            <div className="inbox-state"><strong>No pending invitations</strong>
              <span>When someone shares a task with you, it will appear here.</span></div>
          ) : (
            <section className="inbox-section" aria-labelledby="task-invitations-pending">
              <div className="inbox-section__heading"><div>
                <p className="eyebrow">ACTION NEEDED</p>
                <h2 id="task-invitations-pending">Pending</h2>
                <p>Requests waiting for your response.</p>
              </div><span>{invitations.length}</span></div>

              <ol className="inbox-list">
                    {invitations.map((invitation) => {
                      const status = invitationStatus(invitation);
                      const busy = busyIds.has(invitation.id);
                      const actionable = canUseInvitationActions && isActionableInvitation(invitation);
                      return <li className="inbox-row inbox-invitation-row" key={invitation.id}>
                        <div className="inbox-row__marker" aria-hidden="true" />
                        <div className="inbox-row__content">
                          <div className="inbox-row__title-line"><h3>{invitation.taskTitle}</h3>
                            <span className={`inbox-status inbox-status--${status.toLowerCase()}`}>{status}</span></div>
                          {invitation.unavailableReason && <p className="inbox-row__warning" role="alert">
                            {invitation.unavailableReason}</p>}
                          <div className="inbox-row__meta">
                            <span>Invited by {invitation.inviterUserName}</span>
                            <span>{permissionText(invitation.permission)} access</span>
                            <time dateTime={invitation.createdAt}>{formatDate(invitation.createdAt)}</time>
                          </div>
                        </div>
                        {canUseInvitationActions && <div className="inbox-row__actions">
                          {actionable && invitation.canAccept && <button className="primary-button" disabled={busy} type="button"
                            aria-label={`Accept invitation to ${invitation.taskTitle}`}
                            onClick={() => respond(invitation.id, true)}>Accept</button>}
                          {actionable && invitation.canReject && <button className="secondary-button" disabled={busy} type="button"
                            aria-label={`Reject invitation to ${invitation.taskTitle}`}
                            onClick={() => respond(invitation.id, false)}>Reject</button>}
                          <Link className="secondary-button" to={`/tasks/invitations/${invitation.id}`}
                            aria-label={`View invitation details for ${invitation.taskTitle}`}>View details</Link>
                        </div>}
                      </li>;
                    })}
              </ol>
            </section>
          )}
      </div>
    </main>
  );
}

export default TaskInvitationsPage;
