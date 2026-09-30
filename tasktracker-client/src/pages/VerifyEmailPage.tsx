// import { useState } from "react";
// import { useNavigate } from "react-router-dom";
// import { verifyEmail } from "../api/authService";

// function VerifyEmailPage() {
//   const navigate = useNavigate();

//   const [email, setEmail] = useState("");
//   const [code, setCode] = useState("");

//   const [loading, setLoading] = useState(false);
//   const [error, setError] = useState("");
//   const [successMessage, setSuccessMessage] = useState("");

//   const handleVerifyEmail = async (e: React.FormEvent<HTMLFormElement>) => {
//     e.preventDefault();

//     try {
//       setLoading(true);
//       setError("");
//       setSuccessMessage("");

//       await verifyEmail({
//         email,
//         code,
//       });

//       setSuccessMessage("Email verified successfully. You can now sign in.");

//       setTimeout(() => {
//         navigate("/login");
//       }, 1200);
//     } catch (error) {
//       //console.log(error);
//       setError("Email verification failed.");
//     } finally {
//       setLoading(false);
//     }
//   };

//   return (
//     <main className="page auth-page">
//       <section className="auth-card">
//         <p className="eyebrow">EMAIL VERIFICATION</p>
//         <h1>Verify Email</h1>
//         <p className="auth-text">
//           Enter the verification code sent to your email address.
//         </p>

//         <form onSubmit={handleVerifyEmail} className="auth-form">
//           <div className="form-group">
//             <label>Email</label>
//             <input
//               value={email}
//               onChange={(e) => setEmail(e.target.value)}
//               placeholder="your@email.com"
//               type="email"
//               required
//             />
//           </div>

//           <div className="form-group">
//             <label>Verification code</label>
//             <input
//               value={code}
//               onChange={(e) => setCode(e.target.value)}
//               placeholder="Enter code"
//               required
//             />
//           </div>

//           {error && <p className="error-message">{error}</p>}
//           {successMessage && <p className="success-message">{successMessage}</p>}

//           <button className="primary-button" type="submit" disabled={loading}>
//             {loading ? "Verifying..." : "Verify Email"}
//           </button>
//         </form>
//       </section>
//     </main>
//   );
// }

// export default VerifyEmailPage;


import { useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { resendVerification, verifyEmail } from "../api/authService";

const resendMessage =
  "If the account exists and still requires verification, a new code has been sent.";

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
      ? "Your account was created, but email delivery failed. You can resend the verification code below."
      : "";
  });

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

  const handleVerifyEmail = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    try {
      setLoading(true);
      setError("");
      setSuccessMessage("");

      const canonicalEmail = email.trim().toLowerCase();

      await verifyEmail({
        email: canonicalEmail,
        code,
      });

      setSuccessMessage("Email verified successfully. You can now sign in.");

      setTimeout(() => {
        navigate("/login", { state: { email: canonicalEmail } });
      }, 1200);
    } catch (error) {
      setError("Email verification failed.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="page auth-page">
      <section className="auth-card">
        <p className="eyebrow">EMAIL VERIFICATION</p>

        <h1>Verify Email</h1>

        <p className="auth-text">
          We sent a verification code to your email address. Enter the code
          below to activate your account.
        </p>

        <form onSubmit={handleVerifyEmail} className="auth-form">
          <div className="form-group">
            <label>Email</label>
            <input
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="your@email.com"
              type="email"
              required
            />
          </div>

          <div className="form-group">
            <label>Verification code</label>
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="6-digit code"
              maxLength={6}
              required
            />
          </div>

          {error && <p className="error-message">{error}</p>}

          {successMessage && (
            <p className="success-message">{successMessage}</p>
          )}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Verifying..." : "Verify Email"}
          </button>

          <button
            type="button"
            onClick={handleResend}
            disabled={loading || resendLoading}
          >
            {resendLoading ? "Resending..." : "Resend code"}
          </button>
        </form>

        <p className="auth-bottom-text">
          After verification you will be redirected to the login page.
        </p>
      </section>
    </main>
  );
}

export default VerifyEmailPage;
