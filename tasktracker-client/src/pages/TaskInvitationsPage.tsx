import { Link } from "react-router-dom";

function TaskInvitationsPage() {
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
            Incoming task share invitations will be listed here. You will be
            able to accept or reject collaboration requests from this page.
          </p>

          <section className="task-detail-section">
            <div className="task-section-header">
              <div>
                <p className="eyebrow">PENDING</p>
                <h2>Pending invitations</h2>
              </div>
            </div>

            <div className="activity-timeline">
              <div className="timeline-item">
                <strong>Invitation workflow is ready</strong>
                <span>
                  Backend accept and reject actions will be connected here next.
                </span>
              </div>

              <div className="timeline-item">
                <strong>Task access</strong>
                <span>
                  Accepted invitations will create task sharing access records.
                </span>
              </div>
            </div>
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