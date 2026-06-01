import { Link } from "react-router-dom";

function NotificationsPage() {
  return (
    <main className="page public-page">
      <section className="task-detail-layout">
        <div className="task-detail-main">
          <div className="task-detail-header">
            <div>
              <p className="eyebrow">NOTIFICATIONS</p>
              <h1>Notification Center</h1>
            </div>

            <div className="task-detail-actions">
              <button type="button" className="secondary-button">
                Mark all as read
              </button>
            </div>
          </div>

          <p className="task-detail-description">
            Task invitations, updates and system notifications will appear here.
          </p>

          <section className="task-detail-section">
            <div className="task-section-header">
              <div>
                <p className="eyebrow">RECENT</p>
                <h2>Your notifications</h2>
              </div>
            </div>

            <div className="activity-timeline">
              <div className="timeline-item">
                <strong>Notification center is ready</strong>
                <span>
                  Backend notification flow is prepared. API integration will be
                  added next.
                </span>
              </div>

              <div className="timeline-item">
                <strong>Task invitations</strong>
                <span>
                  Incoming task share invitations will be listed here with
                  accept and reject actions.
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
              <Link to="/tasks/create-task">Create Task</Link>
              <Link to="/profile">Profile</Link>
            </div>
          </div>
        </aside>
      </section>
    </main>
  );
}

export default NotificationsPage;