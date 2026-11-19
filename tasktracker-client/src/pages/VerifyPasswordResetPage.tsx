import { useState } from "react";
import axios from "axios";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { verifyPasswordResetCode } from "../api/authService";
import AuthLayout from "../components/AuthLayout";

type PasswordResetLocationState = {
  email?: unknown;
};

function VerifyPasswordResetPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const locationState = location.state as PasswordResetLocationState | null;
  const email =
    typeof locationState?.email === "string" && locationState.email.trim()
      ? locationState.email.trim().toLowerCase()
      : null;

  if (!email) {
    return <AuthLayout eyebrow="Password recovery" title="Recovery session expired"
      description="Start password recovery again to request a new verification code."
      footer={<p><Link to="/login">Back to sign in</Link></p>}>
      <div className="public-auth-status">
        <span className="public-auth-status__icon" aria-hidden="true">!</span>
        <Link className="primary-button" to="/forgot-password" replace>Start again</Link>
      </div>
    </AuthLayout>;
  }

  const handleCodeChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setCode(event.target.value.replace(/[^0-9]/g, "").slice(0, 6));
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");

    if (!/^[0-9]{6}$/.test(code)) {
      setError("Enter the six-digit verification code.");
      return;
    }

    try {
      setLoading(true);

      const response = await verifyPasswordResetCode({ email, code });

      setCode("");
      navigate("/reset-password", {
        replace: true,
        state: {
          resetToken: response.data.resetToken,
          expiresAt: response.data.expiresAt,
        },
      });
    } catch (requestError: unknown) {
      const message = axios.isAxiosError<{ message?: string }>(requestError)
        ? requestError.response?.data?.message
        : undefined;

      setError(message ?? "Invalid or expired password reset code.");
    } finally {
      setLoading(false);
    }
  };

  return <AuthLayout eyebrow="Password recovery · Step 2 of 3" title="Enter verification code"
    description="Enter the latest six-digit code sent to your email address."
    footer={<p>Need a new code? <Link to="/forgot-password">Start again</Link> · <Link to="/login">Back to sign in</Link></p>}>
        <form onSubmit={handleSubmit} className="auth-form" aria-busy={loading}>
          <div className="form-group">
            <label htmlFor="password-reset-code">Verification code</label>
            <input
              id="password-reset-code"
              value={code}
              onChange={handleCodeChange}
              placeholder="6-digit code"
              type="text"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              pattern="[0-9]{6}"
              disabled={loading}
              aria-describedby="password-reset-code-hint"
              required
            />
            <p className="public-auth-field-hint" id="password-reset-code-hint">Use the most recent code you received.</p>
          </div>

          {error && <p className="error-message" role="alert">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Verifying..." : "Verify code"}
          </button>
        </form>
  </AuthLayout>;
}

export default VerifyPasswordResetPage;
