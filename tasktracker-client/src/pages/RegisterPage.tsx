import { useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { register } from "../api/authService";
import { errorMessage } from "../api/errorMessage";
import AuthLayout from "../components/AuthLayout";

function RegisterPage() {
  const navigate = useNavigate();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [userName, setUserName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const submitting = useRef(false);

  const handleRegister = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submitting.current) return;
    if (password.length < 12 || password.length > 128) {
      setError("Password must be between 12 and 128 characters.");
      return;
    }
    submitting.current = true;
    try {
      setLoading(true);
      setError("");
      const submittedEmail = email.trim();
      const response = await register({
        firstName,
        lastName,
        email: submittedEmail,
        userName: userName.trim(),
        password,
      });
      navigate("/verify-email", { replace: true, state: {
        email: submittedEmail, registrationMessage: response.message,
      } });
    } catch (requestError: unknown) {
      setError(errorMessage(requestError, "Could not confirm registration. Check your inbox before trying again."));
    } finally {
      submitting.current = false;
      setLoading(false);
    }
  };

  return <AuthLayout eyebrow="Create account" title="Start with a clear workspace"
    description="Create your account, verify your email, and begin organizing personal and shared work."
    footer={<p>Already have an account? <Link to="/login">Sign in</Link></p>}>
    <form onSubmit={handleRegister} className="auth-form" aria-busy={loading}>
      <div className="form-grid-two">
        <div className="form-group">
          <label htmlFor="register-first-name">First name</label>
          <input id="register-first-name" value={firstName} onChange={(event) => setFirstName(event.target.value)}
            placeholder="First name" autoComplete="given-name" disabled={loading} required />
        </div>
        <div className="form-group">
          <label htmlFor="register-last-name">Last name</label>
          <input id="register-last-name" value={lastName} onChange={(event) => setLastName(event.target.value)}
            placeholder="Last name" autoComplete="family-name" disabled={loading} required />
        </div>
      </div>
      <div className="form-group">
        <label htmlFor="register-username">Username</label>
        <input id="register-username" value={userName} onChange={(event) => setUserName(event.target.value)}
          placeholder="Choose a username" autoComplete="username" maxLength={50} disabled={loading} required />
        <p className="public-auth-field-hint">Used to identify you in shared tasks and workspaces.</p>
      </div>
      <div className="form-group">
        <label htmlFor="register-email">Email</label>
        <input id="register-email" value={email} onChange={(event) => setEmail(event.target.value)}
          placeholder="you@example.com" type="email" autoComplete="email" disabled={loading} required />
      </div>
      <div className="form-group">
        <label htmlFor="register-password">Password</label>
        <input id="register-password" value={password} onChange={(event) => setPassword(event.target.value)}
          placeholder="Create a password" type="password" autoComplete="new-password"
          minLength={12} maxLength={128} disabled={loading} aria-describedby="register-password-hint" required />
        <p className="public-auth-field-hint" id="register-password-hint">Use 12–128 characters.</p>
      </div>
      {error && <p className="error-message" role="alert">{error}</p>}
      <button className="primary-button" type="submit" disabled={loading}>
        {loading ? "Creating account..." : "Create account"}
      </button>
    </form>
  </AuthLayout>;
}

export default RegisterPage;
