import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { loginAdmin } from "../api/adminService";

function AdminLoginPage() {
  const navigate = useNavigate();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleLogin = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    try {
      setLoading(true);
      setError("");

      await loginAdmin({
        username,
        password,
      });

      sessionStorage.setItem("pendingAdminUsername", username);

      navigate("/secret-admin-entry/verify");
    } catch (error) {
      console.log(error);
      setError("Username or password is wrong.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="page auth-page">
      <section className="auth-card">
        <p className="eyebrow">ADMIN ACCESS</p>
        <h1>Sign in</h1>
        <p className="auth-text">
          Enter your admin credentials. A verification code will be sent to your email.
        </p>

        <form onSubmit={handleLogin} className="auth-form">
          <div className="form-group">
            <label>Username</label>
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="Admin username"
              required
            />
          </div>

          <div className="form-group">
            <label>Password</label>
            <input
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Admin password"
              type="password"
              required
            />
          </div>

          {error && <p className="error-message">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Sending code..." : "Continue"}
          </button>
        </form>
      </section>
    </main>
  );
}

export default AdminLoginPage;