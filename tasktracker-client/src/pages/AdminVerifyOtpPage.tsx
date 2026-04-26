import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { verifyOtp } from "../api/adminService";

function AdminVerifyOtpPage() {
  const navigate = useNavigate();

  const [code, setCode] = useState("");
  const [username, setUsername] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const pendingUsername = sessionStorage.getItem("pendingAdminUsername");

    if (!pendingUsername) {
      navigate("/secret-admin-entry");
      return;
    }

    setUsername(pendingUsername);
  }, [navigate]);

  const handleVerify = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    try {
      setLoading(true);
      setError("");

      const response = await verifyOtp({
        username,
        code,
      });

      sessionStorage.setItem("adminToken", response.adminToken);
      sessionStorage.setItem("adminTokenExpireAt", response.expireAt);
      sessionStorage.removeItem("pendingAdminUsername");

      navigate("/admin-dashboard");
    } catch (error) {
      console.log(error);
      setError("Verification code is wrong or expired.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="page auth-page">
      <section className="auth-card">
        <p className="eyebrow">EMAIL VERIFICATION</p>
        <h1>Enter Code</h1>
        <p className="auth-text">
          We sent a 6 digit verification code to your email.
        </p>

        <form onSubmit={handleVerify} className="auth-form">
          <div className="form-group">
            <label>Verification Code</label>
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="123456"
              maxLength={6}
              required
            />
          </div>

          {error && <p className="error-message">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Checking..." : "Verify"}
          </button>
        </form>
      </section>
    </main>
  );
}

export default AdminVerifyOtpPage;