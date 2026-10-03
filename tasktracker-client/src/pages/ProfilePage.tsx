import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

export default function ProfilePage() {
  const { user } = useAuth();
  const firstLetter = user?.name?.charAt(0).toUpperCase() ?? user?.email?.charAt(0).toUpperCase() ?? "?";
  return <main className="page public-page"><section className="profile-page-layout">
    <aside className="profile-sidebar-card">
      <div className="profile-avatar"><span>{firstLetter}</span></div>
      <h1>{user?.name ?? "Account"}</h1><p>{user?.email ?? "-"}</p>
      <div className="profile-role-pill">{user?.role ?? "User"}</div>
      <div className="profile-action-list">
        <Link to="/settings/security" className="secondary-button">Account Security</Link>
        {user?.role === "Admin" && <Link to="/admin-dashboard" className="secondary-button">Admin dashboard</Link>}
      </div>
    </aside>
    <section className="profile-main-content">
      <section className="profile-content-card">
        <div className="profile-card-header"><div><p className="eyebrow">ACCOUNT</p><h2>Your account</h2></div></div>
        <p>Review your account information and manage your sign-in security.</p>
        <Link to="/settings/security" className="primary-button">Manage security</Link>
      </section>
      <section className="profile-content-card"><h2>Account details</h2>
        <div className="profile-details-grid"><div><span>Name</span><strong>{user?.name ?? "-"}</strong></div>
          <div><span>Email</span><strong>{user?.email ?? "-"}</strong></div>
          <div><span>Role</span><strong>{user?.role ?? "-"}</strong></div></div>
      </section>
    </section>
  </section></main>;
}
