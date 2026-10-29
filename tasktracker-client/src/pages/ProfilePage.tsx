import { Link } from "react-router-dom";
import AccountLocalNav from "../components/AccountLocalNav";
import { useAuth } from "../context/AuthContext";

export default function ProfilePage() {
  const { user } = useAuth();
  const firstLetter = user?.name?.charAt(0).toUpperCase() ?? user?.email?.charAt(0).toUpperCase() ?? "?";

  return (
    <main className="page public-page account-page">
      <div className="account-shell">
        <header className="account-header">
          <div>
            <p className="eyebrow">ACCOUNT</p>
            <h1>Profile</h1>
            <p>Review the identity and account type connected to your sign-in.</p>
          </div>
          {user?.role === "Admin" && (
            <Link to="/admin-dashboard" className="secondary-button">
              Admin dashboard
            </Link>
          )}
        </header>

        <AccountLocalNav />

        <section className="account-section" aria-labelledby="account-identity-title">
          <div className="account-identity">
            <div className="account-avatar" aria-hidden="true">
              {firstLetter}
            </div>
            <div className="account-identity__copy">
              <p className="eyebrow">SIGNED-IN IDENTITY</p>
              <h2 id="account-identity-title">{user?.name ?? "Account"}</h2>
              <p>{user?.email ?? "Email unavailable"}</p>
            </div>
            <span className="account-role">{user?.role ?? "User"} account</span>
          </div>

          <p className="account-readonly-note">
            This information comes from your authenticated account and is read-only here.
          </p>

          <dl className="account-detail-list">
            <div><dt>Email address</dt><dd>{user?.email ?? "Unavailable"}</dd></div>
            <div><dt>Account type</dt><dd>{user?.role ?? "User"}</dd></div>
          </dl>

          <div className="account-section__actions">
            <Link to="/settings/security" className="primary-button">
              Manage security
            </Link>
          </div>
        </section>
      </div>
    </main>
  );
}
