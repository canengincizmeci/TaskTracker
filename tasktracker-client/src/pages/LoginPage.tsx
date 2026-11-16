import { useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import toast from "react-hot-toast";
import { login } from "../api/authService";
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

  const handleLogin = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    try {
      setLoading(true);
      setError("");
      const response = await login({ email: email.trim().toLowerCase(), password });
      const token = response.data.accessToken.token;
      loginToContext(token, response.data.refreshToken, response.data.accessToken.expiration);
      toast.success("Login successful");

      const redirect = getSafeLoginRedirect(new URLSearchParams(location.search).get("redirect"));
      if (redirect) {
        navigate(redirect, { replace: true });
        return;
      }
      navigate(getRoleFromToken(token) === "Admin" ? "/admin-dashboard" : "/dashboard", { replace: true });
    } catch {
      setError("Email or password is wrong.");
      toast.error("Email or password is wrong.");
    } finally {
      setLoading(false);
    }
  };

  return <AuthLayout eyebrow="Account access" title="Welcome back"
    description="Sign in to continue to your tasks, workspaces, and shared work."
    footer={<p>New to TaskTracker? <Link to="/register">Create an account</Link></p>}>
    <form onSubmit={handleLogin} className="auth-form" aria-busy={loading}>
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
      <div className="public-auth-form-row"><Link to="/forgot-password">Forgot password?</Link></div>
      {error && <p className="error-message" role="alert">{error}</p>}
      <button className="primary-button" type="submit" disabled={loading}>
        {loading ? "Signing in..." : "Sign in"}
      </button>
    </form>
  </AuthLayout>;
}

export default LoginPage;
