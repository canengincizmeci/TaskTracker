import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

export default function ProfilePage() {
  const { user } = useAuth();
  const isUser = user?.role === "User";
  const firstLetter = user?.name?.charAt(0).toUpperCase() ?? user?.email?.charAt(0).toUpperCase() ?? "?";
  return <main className="page public-page"><section className="profile-page-layout">
    <aside className="profile-sidebar-card">
      <div className="profile-avatar"><span>{firstLetter}</span></div>
      <h1>{user?.name ?? "Account"}</h1><p>{user?.email ?? "-"}</p>
      <div className="profile-role-pill">{user?.role ?? "User"}</div>
      <div className="profile-action-list">
        <Link to="/" className="secondary-button">Workspace</Link>
        <Link to="/settings/security" className="secondary-button">Account Security</Link>
      </div>
    </aside>
    <section className="profile-main-content">
      <section className="profile-content-card">
        <div className="profile-card-header"><div><p className="eyebrow">ACCOUNT</p><h2>Your workspace</h2></div></div>
        <p>Use the live task views below to see current work. This page does not display estimated or sample totals.</p>
        {isUser && <div className="profile-link-list">
          <Link to="/tasks/user-tasks"><div><strong>Owned Tasks</strong><span>Tasks you created.</span></div><span>→</span></Link>
          <Link to="/tasks/assigned-to-me"><div><strong>Assigned to Me</strong><span>Work you are responsible for.</span></div><span>→</span></Link>
          <Link to="/tasks/shared-tasks"><div><strong>Shared With Me</strong><span>Tasks you can collaborate on.</span></div><span>→</span></Link>
          <Link to="/tasks/create-task"><div><strong>Create Task</strong><span>Start a new piece of work.</span></div><span>→</span></Link>
        </div>}
      </section>
      <section className="profile-content-card"><h2>Account details</h2>
        <div className="profile-details-grid"><div><span>Name</span><strong>{user?.name ?? "-"}</strong></div>
          <div><span>Email</span><strong>{user?.email ?? "-"}</strong></div>
          <div><span>Role</span><strong>{user?.role ?? "-"}</strong></div></div>
      </section>
    </section>
  </section></main>;
}
