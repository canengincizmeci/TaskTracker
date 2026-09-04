import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

function ProfilePage() {
  const { user } = useAuth();

  const firstLetter =
    user?.name?.charAt(0).toUpperCase() ??
    user?.email?.charAt(0).toUpperCase() ??
    "?";

  return (
    <main className="page public-page">
      <section className="profile-page-layout">
        <aside className="profile-sidebar-card">
          <div className="profile-avatar">
            <span>{firstLetter}</span>
          </div>

          <h1>{user?.name ?? "Unknown User"}</h1>

          <p>{user?.email ?? "-"}</p>

          <div className="profile-role-pill">
            {user?.role ?? "User"}
          </div>

          <div className="profile-action-list">
            <Link to="/" className="secondary-button">
              Dashboard
            </Link>

            <Link to="/tasks/user-tasks" className="primary-button">
              My Tasks
            </Link>

            <Link to="/shared-tasks" className="secondary-button">
              Shared Tasks
            </Link>

            <Link to="/create-task" className="secondary-button">
              Create Task
            </Link>

            <Link to="/settings/security" className="secondary-button">
              Account Security
            </Link>
          </div>
        </aside>

        <section className="profile-main-content">
          <div className="profile-overview-card">
            <div className="profile-section-header">
              <div>
                <p className="eyebrow">ACCOUNT OVERVIEW</p>
                <h2>Your workspace</h2>
              </div>

              <Link to="/create-task" className="primary-button">
                New Task
              </Link>
            </div>

            <div className="profile-stats-grid">
              <div className="profile-stat-card">
                <span>Owned tasks</span>
                <strong>12</strong>
              </div>

              <div className="profile-stat-card">
                <span>Shared tasks</span>
                <strong>5</strong>
              </div>

              <div className="profile-stat-card">
                <span>Pending requests</span>
                <strong>2</strong>
              </div>
            </div>
          </div>

          <div className="profile-grid">
            <section className="profile-content-card">
              <div className="profile-card-header">
                <div>
                  <p className="eyebrow">TASK ACCESS</p>
                  <h3>Your task areas</h3>
                </div>
              </div>

              <div className="profile-link-list">
                <Link to="tasks/user-tasks">
                  <div>
                    <strong>My Tasks</strong>
                    <span>Tasks you created and manage.</span>
                  </div>

                  <span>→</span>
                </Link>

                <Link to="/shared-tasks">
                  <div>
                    <strong>Shared With Me</strong>
                    <span>Tasks where another user added you.</span>
                  </div>

                  <span>→</span>
                </Link>

                <Link to="/create-task">
                  <div>
                    <strong>Create Task</strong>
                    <span>Create a new task and assign priority.</span>
                  </div>

                  <span>→</span>
                </Link>
              </div>
            </section>

            <section className="profile-content-card">
              <div className="profile-card-header">
                <div>
                  <p className="eyebrow">NOTIFICATIONS</p>
                  <h3>Recent activity</h3>
                </div>
              </div>

              <div className="activity-list">
                <div className="activity-item">
                  <strong>Task shared with you</strong>
                  <span>
                    Another user added you to a backend integration task.
                  </span>
                </div>

                <div className="activity-item">
                  <strong>Email verified</strong>
                  <span>Your account is fully active.</span>
                </div>

                <div className="activity-item">
                  <strong>Workspace ready</strong>
                  <span>
                    You can now create, manage and share tasks.
                  </span>
                </div>
              </div>
            </section>
          </div>

          <section className="profile-content-card">
            <div className="profile-card-header">
              <div>
                <p className="eyebrow">ACCOUNT DETAILS</p>
                <h3>User information</h3>
              </div>
            </div>

            <div className="profile-details-grid">
              <div>
                <span>Name</span>
                <strong>{user?.name ?? "-"}</strong>
              </div>

              <div>
                <span>Email</span>
                <strong>{user?.email ?? "-"}</strong>
              </div>

              <div>
                <span>Role</span>
                <strong>{user?.role ?? "-"}</strong>
              </div>

              <div>
                <span>Status</span>
                <strong>Active</strong>
              </div>
            </div>
          </section>
        </section>
      </section>
    </main>
  );
}

export default ProfilePage;
