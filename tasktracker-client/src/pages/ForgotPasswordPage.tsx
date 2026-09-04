import { useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import { forgotPassword } from "../api/authService";

function ForgotPasswordPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const trimmedEmail = email.trim();

    if (!trimmedEmail) {
      setError("Email is required.");
      return;
    }

    try {
      setLoading(true);
      setError("");

      const response = await forgotPassword({ email: trimmedEmail });

      toast.success(response.message);
      navigate("/verify-password-reset", {
        state: { email: trimmedEmail },
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

  return (
    <main className="page auth-page">
      <section className="auth-card">
        <p className="eyebrow">PASSWORD RECOVERY</p>
        <h1>Forgot password?</h1>
        <p className="auth-text">
          Enter your email address. If an eligible account exists, password
          recovery instructions will be sent.
        </p>

        <form onSubmit={handleSubmit} className="auth-form">
          <div className="form-group">
            <label htmlFor="forgot-password-email">Email</label>
            <input
              id="forgot-password-email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              placeholder="your@email.com"
              type="email"
              autoComplete="email"
              required
            />
          </div>

          {error && <p className="error-message">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Sending instructions..." : "Continue"}
          </button>
        </form>

        <p className="auth-bottom-text">
          Remember your password? <Link to="/login">Back to login</Link>
        </p>
      </section>
    </main>
  );
}

export default ForgotPasswordPage;
