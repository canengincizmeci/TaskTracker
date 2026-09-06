import { useEffect, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { resetPassword } from "../api/authService";
import { useAuth } from "../context/AuthContext";

function getResetToken(navigationState: unknown): string | null {
  if (
    typeof navigationState !== "object" ||
    navigationState === null ||
    !("resetToken" in navigationState)
  ) {
    return null;
  }

  const { resetToken } = navigationState;
  return typeof resetToken === "string" && resetToken.trim().length > 0
    ? resetToken
    : null;
}

function ResetPasswordPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const { logout } = useAuth();
  const [resetToken] = useState<string | null>(() =>
    getResetToken(location.state)
  );
  const [newPassword, setNewPassword] = useState("");
  const [confirmNewPassword, setConfirmNewPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const hasSensitiveNavigationState = getResetToken(location.state) !== null;

  useEffect(() => {
    if (hasSensitiveNavigationState) {
      navigate(location.pathname, {
        replace: true,
        state: null,
      });
    }
  }, [hasSensitiveNavigationState, location.pathname, navigate]);

  if (!resetToken) {
    return (
      <main className="page auth-page">
        <section className="auth-card">
          <p className="eyebrow">PASSWORD RECOVERY</p>
          <h1>Recovery session unavailable</h1>
          <p className="auth-text">
            This password recovery session is invalid or has expired. Start
            again to request a new verification code.
          </p>
          <Link className="primary-button" to="/forgot-password" replace>
            Start again
          </Link>
          <p className="auth-bottom-text">
            <Link to="/login">Back to login</Link>
          </p>
        </section>
      </main>
    );
  }

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");

    if (!newPassword.trim() || !confirmNewPassword.trim()) {
      setError("New password and confirmation are required.");
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

      const response = await resetPassword({
        resetToken,
        newPassword,
        confirmNewPassword,
      });

      setNewPassword("");
      setConfirmNewPassword("");
      toast.success(response.message);
      logout();
      navigate("/login", { replace: true });
    } catch (requestError: unknown) {
      const message = axios.isAxiosError<{ message?: string }>(requestError)
        ? requestError.response?.data?.message
        : undefined;

      setError(message ?? "Password could not be reset.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="page auth-page">
      <section className="auth-card">
        <p className="eyebrow">PASSWORD RECOVERY</p>
        <h1>Create a new password</h1>
        <p className="auth-text">
          Enter and confirm the new password for your account.
        </p>

        <form onSubmit={handleSubmit} className="auth-form">
          <div className="form-group">
            <label htmlFor="new-password">New password</label>
            <input
              id="new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              type="password"
              autoComplete="new-password"
              maxLength={128}
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="confirm-new-password">Confirm new password</label>
            <input
              id="confirm-new-password"
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
            {loading ? "Resetting password..." : "Reset password"}
          </button>
        </form>

        <p className="auth-bottom-text">
          Need a new code? <Link to="/forgot-password">Start again</Link>
          {" · "}
          <Link to="/login">Back to login</Link>
        </p>
      </section>
    </main>
  );
}

export default ResetPasswordPage;
