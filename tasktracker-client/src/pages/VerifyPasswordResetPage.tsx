import { useState } from "react";
import axios from "axios";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { verifyPasswordResetCode } from "../api/authService";

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
      ? locationState.email
      : null;

  if (!email) {
    return (
      <main className="page auth-page">
        <section className="auth-card">
          <p className="eyebrow">PASSWORD RECOVERY</p>
          <h1>Recovery session expired</h1>
          <p className="auth-text">
            Start password recovery again to request a new verification code.
          </p>
          <Link className="primary-button" to="/forgot-password" replace>
            Start again
          </Link>
        </section>
      </main>
    );
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

  return (
    <main className="page auth-page">
      <section className="auth-card">
        <p className="eyebrow">PASSWORD RECOVERY</p>
        <h1>Enter verification code</h1>
        <p className="auth-text">
          Enter the latest six-digit code sent to your email address.
        </p>

        <form onSubmit={handleSubmit} className="auth-form">
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
              required
            />
          </div>

          {error && <p className="error-message">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Verifying..." : "Verify code"}
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

export default VerifyPasswordResetPage;
