import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  acceptTaskInvitation,
  getMyPendingInvitations,
  rejectTaskInvitation,
} from "../api/taskInvitationService";
import { errorMessage as getErrorMessage } from "../api/errorMessage";
import { permissionText } from "../types/taskPermission";
import type { TaskInvitation } from "../types/taskInvitation";

function TaskInvitationsPage() {
  const navigate = useNavigate();
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

  if (loading) {
    return (
      <main className="page public-page">
        <div className="task-detail-layout">
          <div className="task-detail-main">
            <h2>Loading invitations...</h2>
          </div>
        </div>
      </main>
    );
  }

  return (
    <main className="page public-page">
      <section className="task-detail-layout">
        <div className="task-detail-main">
          <div className="task-detail-header">
            <div>
              <p className="eyebrow">INVITATIONS</p>
              <h1>Task Invitations</h1>
            </div>

            <div className="task-detail-actions">
              <Link to="/notifications" className="secondary-button">
                Notifications
              </Link>
            </div>
          </div>

          <p className="task-detail-description">
            Incoming task share invitations will be listed here. You can accept
            or reject collaboration requests from this page.
          </p>

          {errorMessage && (
            <div role="alert" className="error-message">{errorMessage}</div>
          )}

          <section className="task-detail-section">
            <div className="task-section-header">
              <div>
                <p className="eyebrow">PENDING</p>
                <h2>Pending invitations</h2>
              </div>
            </div>

            {invitations.length === 0 ? (
              <div className="activity-timeline">
                <div className="timeline-item">
                  <strong>No pending invitations</strong>
                  <span>
                    When someone shares a task with you, it will appear here.
                  </span>
                </div>
              </div>
            ) : (
              <div className="activity-timeline">
                {invitations.map((invitation) => (
                  <div
                    key={invitation.id}
                    className="timeline-item"
                  >
                    <strong>{invitation.taskTitle}</strong>
                    {invitation.unavailableReason && <p role="alert">{invitation.unavailableReason}</p>}

                    <span>
                      Invited by: {invitation.inviterUserName}
                    </span>

                    <span>
                      Permission:{" "}
                      {permissionText(invitation.permission)}
                    </span>

                    <span>
                      {new Date(
                        invitation.createdAt
                      ).toLocaleString("tr-TR")}
                    </span>

                    <div
                      style={{
                        display: "flex",
                        gap: "0.75rem",
                        marginTop: "1rem",
                      }}
                    >
                      <button
                        className="primary-button"
                        disabled={busyIds.has(invitation.id) || !invitation.canAccept}
                        onClick={() =>
                          respond(invitation.id, true)
                        }
                      >
                        Accept
                      </button>

                      <button
                        className="secondary-button"
                        disabled={busyIds.has(invitation.id) || !invitation.canReject}
                        onClick={() =>
                          respond(invitation.id, false)
                        }
                      >
                        Reject
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>
        </div>

        <aside className="task-detail-sidebar">
          <div className="task-sidebar-card">
            <div className="task-sidebar-header">
              <p className="eyebrow">QUICK ACCESS</p>
              <h2>Workspace links</h2>
            </div>

            <div className="task-sidebar-links">
              <Link to="/tasks/user-tasks">My Tasks</Link>
              <Link to="/tasks/shared-tasks">Shared With Me</Link>
              <Link to="/notifications">Notifications</Link>
              <Link to="/profile">Profile</Link>
            </div>
          </div>
        </aside>
      </section>
    </main>
  );
}

export default TaskInvitationsPage;
