import { useEffect, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { resetPassword } from "../api/authService";
import AuthLayout from "../components/AuthLayout";
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
    return <AuthLayout eyebrow="Password recovery" title="Recovery session unavailable"
      description="This recovery session is invalid or expired. Start again to request a new verification code."
      footer={<p><Link to="/login">Back to sign in</Link></p>}>
      <div className="public-auth-status">
        <span className="public-auth-status__icon" aria-hidden="true">!</span>
        <Link className="primary-button" to="/forgot-password" replace>Start again</Link>
      </div>
    </AuthLayout>;
  }

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");

    if (!newPassword || !confirmNewPassword) {
      setError("New password and confirmation are required.");
      return;
    }

    if (
      newPassword.length < 12 ||
      newPassword.length > 128 ||
      confirmNewPassword.length < 12 ||
      confirmNewPassword.length > 128
    ) {
      setError("Password must be between 12 and 128 characters.");
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

  return <AuthLayout eyebrow="Password recovery · Step 3 of 3" title="Create a new password"
    description="Choose a new password for your account and confirm it below."
    footer={<p>Need a new code? <Link to="/forgot-password">Start again</Link> · <Link to="/login">Back to sign in</Link></p>}>
        <form onSubmit={handleSubmit} className="auth-form" aria-busy={loading}>
          <div className="form-group">
            <label htmlFor="new-password">New password</label>
            <input
              id="new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              type="password"
              autoComplete="new-password"
              minLength={12}
              maxLength={128}
              disabled={loading}
              aria-describedby="new-password-hint"
              required
            />
            <p className="public-auth-field-hint" id="new-password-hint">Use 12–128 characters.</p>
          </div>

          <div className="form-group">
            <label htmlFor="confirm-new-password">Confirm new password</label>
            <input
              id="confirm-new-password"
              value={confirmNewPassword}
              onChange={(event) => setConfirmNewPassword(event.target.value)}
              type="password"
              autoComplete="new-password"
              minLength={12}
              maxLength={128}
              disabled={loading}
              required
            />
          </div>

          {error && <p className="error-message" role="alert">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Resetting password..." : "Reset password"}
          </button>
        </form>
  </AuthLayout>;
}

export default ResetPasswordPage;
