import { useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import { forgotPassword } from "../api/authService";
import AuthLayout from "../components/AuthLayout";

function ForgotPasswordPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const canonicalEmail = email.trim().toLowerCase();

    if (!canonicalEmail) {
      setError("Email is required.");
      return;
    }

    try {
      setLoading(true);
      setError("");

      const response = await forgotPassword({ email: canonicalEmail });

      toast.success(response.message);
      navigate("/verify-password-reset", {
        state: { email: canonicalEmail },
      });
    } catch (requestError: unknown) {
      const message = axios.isAxiosError<{ message?: string }>(requestError)
        ? requestError.response?.data?.message
        : undefined;

      setError(
        message ?? "Password recovery request could not be completed."
      );
    } finally {
      setLoading(false);
    }
  };

  return <AuthLayout eyebrow="Password recovery" title="Recover your account"
    description="Enter your email. If an eligible account exists, password recovery instructions will be sent."
    footer={<p>Remember your password? <Link to="/login">Back to sign in</Link></p>}>
        <form onSubmit={handleSubmit} className="auth-form" aria-busy={loading}>
          <div className="form-group">
            <label htmlFor="forgot-password-email">Email</label>
            <input
              id="forgot-password-email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              placeholder="your@email.com"
              type="email"
              autoComplete="email"
              disabled={loading}
              required
            />
          </div>

          {error && <p className="error-message" role="alert">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Sending instructions..." : "Continue"}
          </button>
        </form>
  </AuthLayout>;
}

export default ForgotPasswordPage;
