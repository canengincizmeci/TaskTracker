import { useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { resendVerification, verifyEmail } from "../api/authService";
import AuthLayout from "../components/AuthLayout";

const resendMessage = "If the account exists and still requires verification, a new code has been sent.";

function VerifyEmailPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [email, setEmail] = useState(() => {
    const state = location.state as { email?: unknown } | null;
    return typeof state?.email === "string" ? state.email : "";
  });
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [resendLoading, setResendLoading] = useState(false);
  const [error, setError] = useState("");
  const [successMessage, setSuccessMessage] = useState(() => {
    const state = location.state as { deliveryFailed?: unknown } | null;
    return state?.deliveryFailed === true
      ? "Your account was created, but email delivery failed. You can resend the verification code below." : "";
  });

  const handleCodeChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setCode(event.target.value.replace(/[^0-9]/g, "").slice(0, 6));
  };

  const handleResend = async () => {
    const canonicalEmail = email.trim().toLowerCase();
    if (!canonicalEmail) {
      setError("Email is required.");
      return;
    }
    try {
      setResendLoading(true);
      setError("");
      await resendVerification({ email: canonicalEmail });
      setSuccessMessage(resendMessage);
    } catch {
      setError("Verification code could not be resent. Please try again.");
    } finally {
      setResendLoading(false);
    }
  };

  const handleVerifyEmail = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!/^[0-9]{6}$/.test(code)) {
      setError("Enter the six-digit verification code.");
      return;
    }
    try {
      setLoading(true);
      setError("");
      setSuccessMessage("");
      const canonicalEmail = email.trim().toLowerCase();
      await verifyEmail({ email: canonicalEmail, code });
      setSuccessMessage("Email verified successfully. You can now sign in.");
      setTimeout(() => navigate("/login", { state: { email: canonicalEmail } }), 1200);
    } catch {
      setError("Email verification failed.");
    } finally {
      setLoading(false);
    }
  };

  const busy = loading || resendLoading;
  return <AuthLayout eyebrow="Email verification" title="Check your inbox"
    description="Enter the six-digit verification code sent to your email to activate your account."
    footer={<p>Already verified? <Link to="/login">Return to sign in</Link></p>}>
    <form onSubmit={handleVerifyEmail} className="auth-form" aria-busy={busy}>
      <div className="form-group">
        <label htmlFor="verify-email">Email</label>
        <input id="verify-email" value={email} onChange={(event) => setEmail(event.target.value)}
          placeholder="you@example.com" type="email" autoComplete="email" disabled={busy} required />
      </div>
      <div className="form-group">
        <label htmlFor="verification-code">Verification code</label>
        <input id="verification-code" value={code} onChange={handleCodeChange}
          placeholder="6-digit code" inputMode="numeric" autoComplete="one-time-code"
          pattern="[0-9]{6}" maxLength={6} disabled={busy}
          aria-describedby="verification-code-hint" required />
        <p className="public-auth-field-hint" id="verification-code-hint">Use the most recent six-digit code you received.</p>
      </div>
      {error && <p className="error-message" role="alert">{error}</p>}
      {successMessage && <p className="success-message" role="status">{successMessage}</p>}
      <div className="public-auth-inline-actions">
        <button className="primary-button" type="submit" disabled={busy}>
          {loading ? "Verifying..." : "Verify email"}
        </button>
        <button className="secondary-button" type="button" onClick={handleResend} disabled={busy}>
          {resendLoading ? "Resending..." : "Resend code"}
        </button>
      </div>
    </form>
  </AuthLayout>;
}

export default VerifyEmailPage;
