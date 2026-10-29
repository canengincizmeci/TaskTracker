import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import toast from "react-hot-toast";
import { acceptTaskInvitation, rejectTaskInvitation, getTaskInvitation } from "../api/taskInvitationService";
import { errorMessage } from "../api/errorMessage";
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

function InvitationDetails({ id }: { id: number }) {
  const navigate = useNavigate();
  const [invitation, setInvitation] = useState<TaskInvitation | null>(null);
  const [loading, setLoading] = useState(false);
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
    setLoading(true);
    setError("");
    try {
      await (accept ? acceptTaskInvitation(id) : rejectTaskInvitation(id));
      toast.success(accept ? "Invitation accepted" : "Invitation rejected");
      navigate(accept && invitation ? `/tasks/task-detail/${invitation.taskRequestId}` : "/tasks/invitations", { replace: true });
    } catch (reason: unknown) {
      setError(errorMessage(reason, "Could not respond to invitation."));
    } finally {
      busy.current = false;
      setLoading(false);
    }
  };

  const status = invitation ? invitationStatus(invitation) : "";
  const canRespond = status.toLowerCase() === "pending" &&
    Boolean(invitation?.canAccept || invitation?.canReject);

  return (
    <main className="page public-page inbox-page">
      <div className="inbox-shell inbox-shell--detail">
        <Link className="inbox-back-link" to="/tasks/invitations">← Back to task invitations</Link>
        <header className="inbox-header inbox-header--detail">
          <div><p className="eyebrow">TASK INVITATION</p>
            <h1>{invitation?.taskTitle ?? "Task invitation"}</h1>
            <p>Review the invitation context and respond when action is available.</p></div>
          {invitation && <span className={`inbox-status inbox-status--${status.toLowerCase()}`}>{status}</span>}
        </header>

        <InboxLocalNav />

        {error && <p role="alert" className="inbox-alert">{error}</p>}
        {!invitation && !error && <div className="inbox-state" role="status">Loading invitation...</div>}
        {invitation && <section className="inbox-invitation-detail" aria-labelledby="invitation-context-title">
          <div className="inbox-section__heading"><div><p className="eyebrow">CONTEXT</p>
            <h2 id="invitation-context-title">Invitation details</h2></div></div>
          <dl className="inbox-detail-list">
            <div><dt>Task</dt><dd>{invitation.taskTitle}</dd></div>
            <div><dt>Invited by</dt><dd>{invitation.inviterUserName}</dd></div>
            <div><dt>Access</dt><dd>{permissionText(invitation.permission)}</dd></div>
            <div><dt>Received</dt><dd><time dateTime={invitation.createdAt}>{formatDate(invitation.createdAt)}</time></dd></div>
            {invitation.expiresAt && <div><dt>Expires</dt><dd>
              <time dateTime={invitation.expiresAt}>{formatDate(invitation.expiresAt)}</time></dd></div>}
          </dl>
          {invitation.unavailableReason && <p className="inbox-row__warning" role="alert">
            {invitation.unavailableReason}</p>}
          {canRespond ? <div className="inbox-detail-actions">
            {invitation.canAccept && <button className="primary-button" onClick={() => respond(true)} disabled={loading}
              type="button">{loading ? "Processing..." : "Accept invitation"}</button>}
            {invitation.canReject && <button className="secondary-button" onClick={() => respond(false)} disabled={loading}
              type="button">{loading ? "Processing..." : "Reject invitation"}</button>}
          </div> : <p className="inbox-history-note" role="status">This invitation no longer needs a response.</p>}
        </section>}
      </div>
    </main>
  );
}

export default function TaskInvitationDetailPage() {
  const { invitationId } = useParams();
  const id = Number(invitationId);
  return Number.isInteger(id) && id > 0
    ? <InvitationDetails key={id} id={id} />
    : <main className="page public-page inbox-page"><div className="inbox-shell inbox-shell--detail">
      <Link className="inbox-back-link" to="/tasks/invitations">← Back to task invitations</Link>
      <div className="inbox-state inbox-state--error" role="alert"><h1>Invitation unavailable</h1>
        <span>This invitation address is invalid.</span></div>
    </div></main>;
}
