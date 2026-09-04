import { useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import { changePassword } from "../api/authService";
import { useAuth } from "../context/AuthContext";

function SecuritySettingsPage() {
  const navigate = useNavigate();
  const { logout } = useAuth();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmNewPassword, setConfirmNewPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");

    if (
      !currentPassword.trim() ||
      !newPassword.trim() ||
      !confirmNewPassword.trim()
    ) {
      setError("Current password, new password and confirmation are required.");
      return;
    }

    if (newPassword.length > 128 || confirmNewPassword.length > 128) {
      setError("New password cannot exceed 128 characters.");
      return;
    }

    if (newPassword !== confirmNewPassword) {
      setError("New password and confirmation do not match.");
      return;
    }

    try {
      setLoading(true);

      const response = await changePassword({
        currentPassword,
        newPassword,
        confirmNewPassword,
      });

      setCurrentPassword("");
      setNewPassword("");
      setConfirmNewPassword("");
      toast.success(response.message);
      logout();
      navigate("/login", { replace: true });
    } catch (requestError: unknown) {
      const message = axios.isAxiosError<{ message?: string }>(requestError)
        ? requestError.response?.data?.message
        : undefined;

      setError(message ?? "Password could not be changed.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="page auth-page">
      <section className="auth-card">
        <p className="eyebrow">ACCOUNT SECURITY</p>
        <h1>Change password</h1>
        <p className="auth-text">
          Changing your password will require you to sign in again.
        </p>

        <form onSubmit={handleSubmit} className="auth-form">
          <div className="form-group">
            <label htmlFor="current-password">Current password</label>
            <input
              id="current-password"
              value={currentPassword}
              onChange={(event) => setCurrentPassword(event.target.value)}
              type="password"
              autoComplete="current-password"
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="security-new-password">New password</label>
            <input
              id="security-new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              type="password"
              autoComplete="new-password"
              maxLength={128}
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="security-confirm-new-password">
              Confirm new password
            </label>
            <input
              id="security-confirm-new-password"
              value={confirmNewPassword}
              onChange={(event) => setConfirmNewPassword(event.target.value)}
              type="password"
              autoComplete="new-password"
              maxLength={128}
              required
            />
          </div>

          {error && <p className="error-message">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Changing password..." : "Change password"}
          </button>
        </form>

        <p className="auth-bottom-text">
          <Link to="/profile">Back to profile</Link>
        </p>
      </section>
    </main>
  );
}

export default SecuritySettingsPage;
