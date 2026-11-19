import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getProfile } from "../api/authService";
import AccountLocalNav from "../components/AccountLocalNav";
import { useAuth } from "../context/AuthContext";

export default function ProfilePage() {
  const { user, token } = useAuth();
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

          <ProfileUsername key={token} />

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

function ProfileUsername() {
  const [username, setUsername] = useState("");
  const [attempt, setAttempt] = useState(0);
  const [failure, setFailure] = useState(false);
  const [copyStatus, setCopyStatus] = useState("");

  useEffect(() => {
    const request = new AbortController();
    void getProfile(request.signal).then(profile => {
      if (!request.signal.aborted) setUsername(profile.username);
    }).catch(() => {
      if (!request.signal.aborted) setFailure(true);
    });
    return () => request.abort();
  }, [attempt]);

  async function copyUsername() {
    try {
      await navigator.clipboard.writeText(username);
      setCopyStatus("Username copied.");
    } catch {
      setCopyStatus("Could not copy automatically. Select the username to copy it.");
    }
  }

  return <div className="account-username">
    <p className="eyebrow">TASKTRACKER USERNAME</p>
    {username ? <>
      <div className="account-username__row">
        <strong>{username}</strong>
        <button type="button" className="secondary-button" onClick={() => void copyUsername()}>Copy username</button>
      </div>
      <p>Share this exact username to receive task invitations.</p>
      <span role="status">{copyStatus}</span>
    </> : failure ? <p role="alert">Could not load your username.{" "}
      <button type="button" className="secondary-button" onClick={() => { setFailure(false); setAttempt(value => value + 1); }}>Retry</button>
    </p> : <p role="status">Loading username…</p>}
  </div>;
}
