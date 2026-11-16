import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import toast from "react-hot-toast";
import { acceptTaskInvitation, rejectTaskInvitation, getTaskInvitation } from "../api/taskInvitationService";
import { errorMessage } from "../api/errorMessage";
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

function statusKey(status: string) {
  return status.toLowerCase().replaceAll(" ", "-");
}

function statusMessage(status: string) {
  switch (status.toLowerCase()) {
    case "pending": return "Your response is needed before this invitation expires.";
    case "accepted": return "You accepted this invitation. Access is governed by the permission shown below.";
    case "rejected": return "You rejected this invitation. No task access was granted.";
    case "expired": return "This invitation passed its expiry time and can no longer be accepted or rejected.";
    default: return "This invitation is no longer awaiting a response.";
  }
}

function InvitationDetails({ id }: { id: number }) {
  const navigate = useNavigate();
  const { user } = useAuth();
  const [invitation, setInvitation] = useState<TaskInvitation | null>(null);
  const [action, setAction] = useState<"accept" | "reject" | null>(null);
  const [error, setError] = useState("");
  const busy = useRef(false);

  useEffect(() => {
    let cancelled = false;
    getTaskInvitation(id).then((data) => {
      if (!cancelled) setInvitation(data);
    }).catch((reason: unknown) => {
      if (!cancelled) setError(errorMessage(reason, "Could not load invitation."));
    });
    return () => { cancelled = true; };
  }, [id]);

  const respond = async (accept: boolean) => {
    if (busy.current) return;
    busy.current = true;
    setAction(accept ? "accept" : "reject");
    setError("");
    try {
      await (accept ? acceptTaskInvitation(id) : rejectTaskInvitation(id));
      toast.success(accept ? "Invitation accepted" : "Invitation rejected");
      navigate(accept && invitation ? `/tasks/task-detail/${invitation.taskRequestId}` : "/tasks/invitations", { replace: true });
    } catch (reason: unknown) {
      setError(errorMessage(reason, "Could not respond to invitation."));
    } finally {
      busy.current = false;
      setAction(null);
    }
  };

  const status = invitation ? invitationStatus(invitation) : "";
  const canRespond = user?.role === "User" && status.toLowerCase() === "pending" &&
    Boolean(invitation?.canAccept || invitation?.canReject);

  return (
    <main className="page public-page inbox-page invitation-detail-page">
      <div className="inbox-shell inbox-shell--detail">
        <Link className="inbox-back-link" to="/tasks/invitations">← Back to task invitations</Link>
        <header className="inbox-header inbox-header--detail">
          <div>
            <p className="eyebrow">TASK INVITATION</p>
            <h1>{invitation?.taskTitle ?? "Task invitation"}</h1>
            <p>Review who invited you, the access offered, and the response state.</p>
          </div>
          {invitation && <span className={`inbox-status inbox-status--${statusKey(status)}`}>{status}</span>}
        </header>

        <InboxLocalNav />

        {error && <p role="alert" className="inbox-alert">{error}</p>}
        {!invitation && !error && <div className="inbox-state" role="status">Loading invitation...</div>}
        {invitation && <>
          <section className={`invitation-status-summary invitation-status-summary--${statusKey(status)}`} aria-label={`Invitation status: ${status}`}>
            <span aria-hidden="true" />
            <div><strong>{status}</strong><p>{statusMessage(status)}</p></div>
          </section>

          <section className="inbox-invitation-detail" aria-labelledby="invitation-context-title">
            <div className="inbox-section__heading">
              <div><p className="eyebrow">CONTEXT</p><h2 id="invitation-context-title">Invitation details</h2>
                <p>Access applies only to the task named here.</p></div>
            </div>
            <dl className="inbox-detail-list">
              <div><dt>Task</dt><dd>{invitation.taskTitle}</dd></div>
              <div><dt>Invited by</dt><dd>{invitation.inviterUserName}</dd></div>
              <div><dt>Permission</dt><dd>{permissionText(invitation.permission)} access</dd></div>
              <div><dt>Status</dt><dd>{status}</dd></div>
              <div><dt>Received</dt><dd><time dateTime={invitation.createdAt}>{formatDate(invitation.createdAt)}</time></dd></div>
              {invitation.expiresAt && <div><dt>Expires</dt><dd>
                <time dateTime={invitation.expiresAt}>{formatDate(invitation.expiresAt)}</time></dd></div>}
            </dl>
            {invitation.unavailableReason && <p className="inbox-row__warning" role="alert">{invitation.unavailableReason}</p>}
            {canRespond ? <div className="inbox-detail-response">
              <div><strong>Choose a response</strong><p>Accept to receive the offered access, or reject to decline it.</p></div>
              <div className="inbox-detail-actions">
                {invitation.canAccept && <button className="primary-button" onClick={() => respond(true)} disabled={action !== null}
                  type="button">{action === "accept" ? "Accepting..." : "Accept invitation"}</button>}
                {invitation.canReject && <button className="secondary-button" onClick={() => respond(false)} disabled={action !== null}
                  type="button">{action === "reject" ? "Rejecting..." : "Reject invitation"}</button>}
              </div>
            </div> : <p className="inbox-history-note" role="status">This invitation no longer needs a response.</p>}
          </section>
        </>}
      </div>
    </main>
  );
}

export default function TaskInvitationDetailPage() {
  const { invitationId } = useParams();
  const id = Number(invitationId);
  return Number.isInteger(id) && id > 0
    ? <InvitationDetails key={id} id={id} />
    : <main className="page public-page inbox-page invitation-detail-page"><div className="inbox-shell inbox-shell--detail">
      <Link className="inbox-back-link" to="/tasks/invitations">← Back to task invitations</Link>
      <div className="inbox-state inbox-state--error" role="alert"><h1>Invitation unavailable</h1>
        <span>This invitation address is invalid.</span></div>
    </div></main>;
}
