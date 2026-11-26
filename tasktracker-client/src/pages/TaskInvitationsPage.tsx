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
import { invitationStatus, useInvitationDeadline } from "../hooks/useInvitationDeadline";

function formatDate(value: string) {
  return new Date(value).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

function isActionableInvitation(invitation: TaskInvitation) {
  return invitationStatus(invitation).toLowerCase() === "pending" &&
    (invitation.canAccept || invitation.canReject);
}

function Invitations({ canUseInvitationActions }: { canUseInvitationActions: boolean }) {
  const navigate = useNavigate();
  const [invitations, setInvitations] = useState<TaskInvitation[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState("");
  const [reload, setReload] = useState(0);
  const mounted = useRef(false);
  useInvitationDeadline(invitations, () => setReload(n => n + 1));

  useEffect(() => {
    let cancelled = false;
    mounted.current = true;
    getMyPendingInvitations().then((data) => {
      if (!cancelled) setInvitations(data);
    }).catch((error: unknown) => {
      if (!cancelled) setErrorMessage(getErrorMessage(error, "Could not load invitations."));
    }).finally(() => {
      if (!cancelled) setLoading(false);
    });
    return () => { cancelled = true; mounted.current = false; };
  }, [reload]);

  const activeRequests = useRef(new Set<number>());
  const [busyIds, setBusyIds] = useState<Set<number>>(new Set());

  const respond = async (id: number, accept: boolean) => {
    if (activeRequests.current.has(id)) return;
    const invite = invitations.find(item => item.id === id);
    if (!invite || !canUseInvitationActions || !isActionableInvitation(invite) || !(accept ? invite.canAccept : invite.canReject)) {
      setReload(n => n + 1); return;
    }
    activeRequests.current.add(id);
    setBusyIds(new Set(activeRequests.current));
    setErrorMessage("");
    try {
      await (accept ? acceptTaskInvitation(id) : rejectTaskInvitation(id));
      if (!mounted.current) return;
      setInvitations((current) => current.filter((item) => item.id !== id));
      if (accept && invite.taskRequestId !== null) navigate(`/tasks/task-detail/${invite.taskRequestId}`);
    } catch (error: unknown) {
      if (mounted.current) {
        setErrorMessage(getErrorMessage(error, "Could not confirm your response. Check the invitation details."));
        setReload(n => n + 1);
      }
    } finally {
      activeRequests.current.delete(id);
      if (mounted.current) setBusyIds(new Set(activeRequests.current));
    }
  };

  const pendingInvitations = invitations.filter(isActionableInvitation);

  return (
    <main className="page public-page inbox-page">
      <div className="inbox-shell">
        <header className="inbox-header">
          <div><p className="eyebrow">INBOX</p><h1>Task invitations</h1>
            <p>Review requests to collaborate on tasks shared with you.</p></div>
          {!loading && !errorMessage && <span className="inbox-count">{pendingInvitations.length} pending</span>}
        </header>

        <InboxLocalNav />

        {errorMessage && <div role="alert" className="inbox-alert">{errorMessage}{" "}
          <button type="button" className="tw-inline-action" onClick={() => {
            setErrorMessage(""); setLoading(true); setReload(n => n + 1);
          }}>Reload invitations</button></div>}
        {loading ? <div className="inbox-state" role="status">Loading invitations...</div> :
          invitations.length === 0 ? (
            !errorMessage && <div className="inbox-state"><strong>No pending invitations</strong>
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
                      const title = invitation.taskTitle ?? "Task invitation";
                      return <li className="inbox-row inbox-invitation-row" key={invitation.id}>
                        <div className="inbox-row__marker" aria-hidden="true" />
                        <div className="inbox-row__content">
                          <div className="inbox-row__title-line"><h3>{title}</h3>
                            <span className={`inbox-status inbox-status--${status.toLowerCase()}`}>{status}</span></div>
                          {invitation.unavailableReason && <p className="inbox-row__warning" role="alert">
                            {invitation.unavailableReason}</p>}
                          <div className="inbox-row__meta">
                            {invitation.inviterUserName && <span>Invited by {invitation.inviterUserName}</span>}
                            <span>{permissionText(invitation.permission)} access</span>
                            <time dateTime={invitation.createdAt}>{formatDate(invitation.createdAt)}</time>
                          </div>
                          {invitation.message && <p className="invitation-note-preview">{invitation.message}</p>}
                        </div>
                        {canUseInvitationActions && <div className="inbox-row__actions">
                          {actionable && !invitation.message && invitation.canAccept && <button className="primary-button" disabled={busy} type="button"
                            aria-label={`Accept invitation to ${title}`}
                            onClick={() => respond(invitation.id, true)}>Accept</button>}
                          {actionable && !invitation.message && invitation.canReject && <button className="secondary-button" disabled={busy} type="button"
                            aria-label={`Reject invitation to ${title}`}
                            onClick={() => respond(invitation.id, false)}>Reject</button>}
                          <Link className={invitation.message ? "primary-button" : "secondary-button"} to={`/tasks/invitations/${invitation.id}`}
                            aria-label={`${actionable ? "Review and reply to" : "View"} invitation for ${title}`}>{actionable ? "Review & reply" : "View details"}</Link>
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

export default function TaskInvitationsPage() {
  const { token, user } = useAuth();
  return <Invitations key={token} canUseInvitationActions={user?.role === "User"} />;
}
