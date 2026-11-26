import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import toast from "react-hot-toast";
import { respondToTaskInvitation, getTaskInvitation } from "../api/taskInvitationService";
import { errorMessage } from "../api/errorMessage";
import { useAuth } from "../context/AuthContext";
import { permissionText } from "../types/taskPermission";
import type { TaskInvitation } from "../types/taskInvitation";
import InboxLocalNav from "../components/InboxLocalNav";
import { invitationStatus, useInvitationDeadline } from "../hooks/useInvitationDeadline";

function formatDate(value: string) {
  return new Date(value).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

function statusKey(status: string) {
  return status.toLowerCase().replaceAll(" ", "-");
}

function statusMessage(status: string) {
  switch (status.toLowerCase()) {
    case "pending": return "Your response is needed before this invitation expires.";
    case "accepted": return "You accepted this invitation. The task owner manages assignment.";
    case "rejected": return "You declined this invitation.";
    case "cancelled": return "The sender withdrew this invitation.";
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
  const [responseMessage, setResponseMessage] = useState("");
  const busy = useRef(false);
  const mounted = useRef(false);
  const request = useRef(0);
  const load = useCallback(async () => {
    const seq = ++request.current;
    try {
      const data = await getTaskInvitation(id);
      if (mounted.current && seq === request.current) { setInvitation(data); setError(""); }
    } catch (reason) {
      if (mounted.current && seq === request.current) {
        setInvitation(null); setError(errorMessage(reason, "Could not load invitation."));
      }
    }
  }, [id]);
  useEffect(() => {
    mounted.current = true;
    const refresh = () => { if (!busy.current) void load(); };
    refresh(); window.addEventListener("focus", refresh);
    return () => { mounted.current = false; window.removeEventListener("focus", refresh); };
  }, [load]);
  useInvitationDeadline(invitation ? [invitation] : [], () => { if (!busy.current) void load(); });

  const respond = async (accept: boolean) => {
    if (busy.current || !invitation || user?.role !== "User") return;
    if (invitationStatus(invitation) !== "Pending" || !(accept ? invitation.canAccept : invitation.canReject)) {
      await load(); return;
    }
    busy.current = true;
    request.current++;
    setAction(accept ? "accept" : "reject");
    setError("");
    try {
      await respondToTaskInvitation(id, accept, responseMessage);
      if (!mounted.current) return;
      toast.success(accept ? "Invitation accepted" : "Invitation rejected");
      navigate(accept && invitation.taskRequestId !== null ? `/tasks/task-detail/${invitation.taskRequestId}` : "/tasks/invitations", { replace: true });
    } catch (reason: unknown) {
      if (!mounted.current) return;
      await load();
      if (mounted.current) setError(errorMessage(reason, "Could not confirm your response. Check the current invitation status."));
    } finally {
      busy.current = false;
      if (mounted.current) setAction(null);
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
          </div>
          {invitation && <span className={`inbox-status inbox-status--${statusKey(status)}`}>{status}</span>}
        </header>

        <InboxLocalNav />

        {error && <div role="alert" className="inbox-alert">{error}{" "}
          <button className="tw-inline-action" type="button" disabled={action !== null} onClick={() => void load()}>Reload invitation</button></div>}
        {!invitation && !error && <div className="inbox-state" role="status">Loading invitation...</div>}
        {invitation && <>
          <section className={`invitation-status-summary invitation-status-summary--${statusKey(status)}`} aria-label={`Invitation status: ${status}`}>
            <span aria-hidden="true" />
            <div><strong>{status}</strong><p>{statusMessage(status)}</p></div>
          </section>

          <section className="inbox-invitation-detail" aria-labelledby="invitation-context-title">
            <div className="inbox-section__heading">
              <h2 id="invitation-context-title">Invitation details</h2>
            </div>
            <dl className="inbox-detail-list">
              {invitation.inviterUserName && <div><dt>Invited by</dt><dd>{invitation.inviterUserName}</dd></div>}
              <div><dt>Permission</dt><dd>{permissionText(invitation.permission)} access</dd></div>
              <div><dt>Received</dt><dd><time dateTime={invitation.createdAt}>{formatDate(invitation.createdAt)}</time></dd></div>
              {invitation.expiresAt && (status === "Pending" || status === "Expired") && <div><dt>Expires</dt><dd>
                <time dateTime={invitation.expiresAt}>{formatDate(invitation.expiresAt)}</time></dd></div>}
            </dl>
            {invitation.message && <div className="invitation-personal-note"><strong>Message from {invitation.inviterUserName}</strong>
              <p>{invitation.message}</p></div>}
            {invitation.responseMessage && <div className="invitation-personal-note"><strong>Your response</strong>
              <p>{invitation.responseMessage}</p></div>}
            {status === "Pending" && invitation.unavailableReason && <p className="inbox-row__warning" role="alert">{invitation.unavailableReason}</p>}
            {canRespond ? <div className="inbox-detail-response">
              <p>Accept to join this task. The owner can assign the work separately.</p>
              <label htmlFor="invitation-response">Optional note to the sender</label>
              <textarea id="invitation-response" rows={3} maxLength={1000} value={responseMessage} disabled={action !== null}
                placeholder="Add availability, context, or a short reply" onChange={(event) => setResponseMessage(event.target.value)} />
              <div className="inbox-detail-actions">
                {invitation.canAccept && <button className="primary-button" onClick={() => respond(true)} disabled={action !== null}
                  type="button">{action === "accept" ? "Accepting..." : "Accept invitation"}</button>}
                {invitation.canReject && <button className="secondary-button" onClick={() => respond(false)} disabled={action !== null}
                  type="button">{action === "reject" ? "Rejecting..." : "Reject invitation"}</button>}
              </div>
            </div> : status === "Accepted" && invitation.taskRequestId !== null &&
              <Link className="primary-button" to={`/tasks/task-detail/${invitation.taskRequestId}`}>Open task</Link>}
          </section>
        </>}
      </div>
    </main>
  );
}

export default function TaskInvitationDetailPage() {
  const { invitationId } = useParams();
  const { token } = useAuth();
  const id = Number(invitationId);
  return Number.isInteger(id) && id > 0
    ? <InvitationDetails key={`${id}:${token}`} id={id} />
    : <main className="page public-page inbox-page invitation-detail-page"><div className="inbox-shell inbox-shell--detail">
      <Link className="inbox-back-link" to="/tasks/invitations">← Back to task invitations</Link>
      <div className="inbox-state inbox-state--error" role="alert"><h1>Invitation unavailable</h1>
        <span>This invitation address is invalid.</span></div>
    </div></main>;
}
