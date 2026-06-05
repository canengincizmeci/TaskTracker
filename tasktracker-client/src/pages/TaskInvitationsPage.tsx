import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import {
  acceptTaskInvitation,
  getMyPendingInvitations,
  rejectTaskInvitation,
} from "../api/taskInvitationService";
import type { TaskInvitation } from "../types/taskInvitation";

function TaskInvitationsPage() {
  const [invitations, setInvitations] = useState<TaskInvitation[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState("");

  const loadInvitations = async () => {
    try {
      setLoading(true);
      setErrorMessage("");

      const data = await getMyPendingInvitations();

      setInvitations(data);
    } catch (error: any) {
      const data = error.response?.data;

      const message =
        typeof data === "string"
          ? data
          : data?.message
          ? data.message
          : data?.title
          ? data.title
          : "An error occurred while loading invitations.";

      setErrorMessage(message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadInvitations();
  }, []);

  const handleAccept = async (invitationId: number) => {
    try {
      await acceptTaskInvitation(invitationId);

      await loadInvitations();
    } catch (error) {
      console.error(error);
    }
  };

  const handleReject = async (invitationId: number) => {
    try {
      await rejectTaskInvitation(invitationId);

      await loadInvitations();
    } catch (error) {
      console.error(error);
    }
  };

  const getPermissionText = (permission: number) => {
    if (permission === 0) return "View";
    if (permission === 1) return "Edit";

    return "Unknown";
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
            <div className="error-message">{errorMessage}</div>
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

                    <span>
                      Invited by: {invitation.inviterUserName}
                    </span>

                    <span>
                      Permission:{" "}
                      {getPermissionText(invitation.permission)}
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
                        onClick={() =>
                          handleAccept(invitation.id)
                        }
                      >
                        Accept
                      </button>

                      <button
                        className="secondary-button"
                        onClick={() =>
                          handleReject(invitation.id)
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