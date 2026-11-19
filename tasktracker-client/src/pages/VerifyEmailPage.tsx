import { useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { resendVerification, verifyEmail } from "../api/authService";
import { errorMessage } from "../api/errorMessage";
import AuthLayout from "../components/AuthLayout";
import { getSafeLoginRedirect } from "../utils/loginRedirect";

function VerifyEmailPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const state = location.state as { email?: unknown; redirect?: unknown; deliveryFailed?: unknown; verificationRequired?: unknown } | null;
  const redirect = getSafeLoginRedirect(typeof state?.redirect === "string" ? state.redirect : null);
  const loginPath = redirect ? `/login?${new URLSearchParams({ redirect })}` : "/login";
  const [email, setEmail] = useState(typeof state?.email === "string" ? state.email : "");
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [resendLoading, setResendLoading] = useState(false);
  const [error, setError] = useState("");
  const submitting = useRef(false);
  const [successMessage, setSuccessMessage] = useState(() => {
    return state?.deliveryFailed === true
      ? "Your account was created, but email delivery failed. You can resend the verification code below."
      : state?.verificationRequired === true ? "Verify your email before signing in." : "";
  });

  const handleCodeChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setCode(event.target.value.replace(/[^0-9]/g, "").slice(0, 6));
  };

  const handleResend = async () => {
    if (submitting.current || !emailInput.current?.reportValidity()) return;
    submitting.current = true;
    try {
      setResendLoading(true);
      setError("");
      setSuccessMessage("");
      const response = await resendVerification({ email: email.trim() });
      setSuccessMessage(response.message);
    } catch (reason) {
      setError(errorMessage(reason, "Verification code could not be resent. Please try again."));
    } finally {
      submitting.current = false;
      setResendLoading(false);
    }
  };

  const handleVerifyEmail = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submitting.current) return;
    if (!/^[0-9]{6}$/.test(code)) {
      setError("Enter the six-digit verification code.");
      return;
    }
    submitting.current = true;
    try {
      setLoading(true);
      setError("");
      setSuccessMessage("");
      const submittedEmail = email.trim();
      await verifyEmail({ email: submittedEmail, code });
      navigate(loginPath, { replace: true, state: { email: submittedEmail, verified: true } });
    } catch (reason) {
      setError(errorMessage(reason, "Could not verify your email. Check your connection and try again."));
    } finally {
      submitting.current = false;
      setLoading(false);
    }
  };

  const busy = loading || resendLoading;
  const emailInput = useRef<HTMLInputElement>(null);
  return <AuthLayout eyebrow="Email verification" title="Check your inbox"
    description="Enter the six-digit verification code sent to your email to activate your account."
    footer={<p>Already verified? <Link to={loginPath} state={{ email: email.trim() }}>Return to sign in</Link></p>}>
    <form onSubmit={handleVerifyEmail} className="auth-form" aria-busy={busy}>
      <div className="form-group">
        <label htmlFor="verify-email">Email</label>
        <input id="verify-email" ref={emailInput} value={email} onChange={(event) => setEmail(event.target.value)}
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
