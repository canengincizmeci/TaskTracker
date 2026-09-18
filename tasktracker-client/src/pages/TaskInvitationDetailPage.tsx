import { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import toast from "react-hot-toast";
import { acceptTaskInvitation, rejectTaskInvitation, getTaskInvitation } from "../api/taskInvitationService";
import { errorMessage } from "../api/errorMessage";
import { permissionText } from "../types/taskPermission";
import type { TaskInvitation } from "../types/taskInvitation";

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
      navigate(accept ? "/tasks/shared-tasks" : "/tasks/invitations", { replace: true });
    } catch (reason: unknown) {
      setError(errorMessage(reason, "Could not respond to invitation."));
    } finally {
      busy.current = false;
      setLoading(false);
    }
  };

  return (
    <main className="task-detail-page">
      <section className="task-detail-card">
        <p className="eyebrow">TASK INVITATION</p>
        <h1>{invitation?.taskTitle ?? "Task invitation"}</h1>
        {error && <p role="alert" className="error-message">{error}</p>}
        {!invitation && !error && <p>Loading invitation...</p>}
        {invitation && <>
          <p>Invited by: {invitation.inviterUserName}</p>
          <p>Permission: {permissionText(invitation.permission)}</p>
          <p>Status: {invitation.status}</p>
          {invitation.unavailableReason && <p role="alert">{invitation.unavailableReason}</p>}
          <div className="task-detail-actions">
            <button className="primary-button" onClick={() => respond(true)} disabled={loading || !invitation.canAccept}>
              {loading ? "Processing..." : "Accept Invitation"}
            </button>
            <button className="secondary-button" onClick={() => respond(false)} disabled={loading || !invitation.canReject}>
              Reject Invitation
            </button>
          </div>
        </>}
      </section>
    </main>
  );
}

export default function TaskInvitationDetailPage() {
  const { invitationId } = useParams();
  const id = Number(invitationId);
  return Number.isInteger(id) && id > 0
    ? <InvitationDetails key={id} id={id} />
    : <p role="alert">Invalid invitation id.</p>;
}
