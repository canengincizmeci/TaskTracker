import { useRef, useState } from "react";
import { isAxiosError } from "axios";
import { Link, useLocation, useNavigate } from "react-router-dom";
import toast from "react-hot-toast";
import { login } from "../api/authService";
import { errorMessage } from "../api/errorMessage";
import AuthLayout from "../components/AuthLayout";
import { useAuth } from "../context/AuthContext";
import { getRoleFromToken } from "../utils/jwtHelper";
import { getSafeLoginRedirect } from "../utils/loginRedirect";

function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { loginToContext } = useAuth();
  const [email, setEmail] = useState(() => {
    const state = location.state as { email?: unknown } | null;
    return typeof state?.email === "string" ? state.email : "";
  });
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const submitting = useRef(false);
  const redirect = getSafeLoginRedirect(new URLSearchParams(location.search).get("redirect"));
  const verified = (location.state as { verified?: unknown } | null)?.verified === true;

  const handleLogin = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submitting.current) return;
    submitting.current = true;
    const submittedEmail = email.trim();
    try {
      setLoading(true);
      setError("");
      const response = await login({ email: submittedEmail, password });
      const token = response.data.accessToken.token;
      loginToContext(token, response.data.refreshToken, response.data.accessToken.expiration);
      toast.success("Login successful");

      if (redirect) {
        navigate(redirect, { replace: true });
        return;
      }
      navigate(getRoleFromToken(token) === "Admin" ? "/admin-dashboard" : "/dashboard", { replace: true });
    } catch (reason) {
      if (isAxiosError(reason) && reason.response?.data?.code === "email_not_verified") {
        navigate("/verify-email", { state: { email: submittedEmail, redirect, verificationRequired: true } });
        return;
      }
      setError(errorMessage(reason, "Could not sign in. Check your connection and try again."));
    } finally {
      submitting.current = false;
      setLoading(false);
    }
  };

  return <AuthLayout eyebrow="Account access" title="Welcome back"
    description="Sign in to continue to your tasks, workspaces, and shared work."
    footer={<p>New to TaskTracker? <Link to="/register">Create an account</Link></p>}>
    <form onSubmit={handleLogin} className="auth-form" aria-busy={loading}>
      {verified && <p className="success-message" role="status">Email verified. You can now sign in.</p>}
      <div className="form-group">
        <label htmlFor="login-email">Email</label>
        <input id="login-email" value={email} onChange={(event) => setEmail(event.target.value)}
          placeholder="you@example.com" type="email" autoComplete="email" disabled={loading} required />
      </div>
      <div className="form-group">
        <label htmlFor="login-password">Password</label>
        <input id="login-password" value={password} onChange={(event) => setPassword(event.target.value)}
          placeholder="Enter your password" type="password" autoComplete="current-password" disabled={loading} required />
      </div>
      <div className="public-auth-form-row">
        <Link to="/forgot-password">Forgot password?</Link>{" · "}
        <Link to="/verify-email" state={{ email: email.trim(), redirect }}>Verify your email</Link>
      </div>
      {error && <p className="error-message" role="alert">{error}</p>}
      <button className="primary-button" type="submit" disabled={loading}>
        {loading ? "Signing in..." : "Sign in"}
      </button>
    </form>
  </AuthLayout>;
}

export default LoginPage;
