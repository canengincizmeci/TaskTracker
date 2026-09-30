// import { useState } from "react";
// import { useNavigate } from "react-router-dom";
// import { register } from "../api/authService";

// function RegisterPage() {
//   const navigate = useNavigate();

//   const [firstName, setFirstName] = useState("");
//   const [lastName, setLastName] = useState("");
//   const [userName, setUserName] = useState("");
//   const [email, setEmail] = useState("");
//   const [password, setPassword] = useState("");

//   const [loading, setLoading] = useState(false);
//   const [error, setError] = useState("");
//   const [successMessage, setSuccessMessage] = useState("");

//   const handleRegister = async (e: React.FormEvent<HTMLFormElement>) => {
//     e.preventDefault();

//     try {
//       setLoading(true);
//       setError("");
//       setSuccessMessage("");

//       await register({
//         firstName,
//         lastName,
//         email,
//         userName,
//         password,
//       });

//       setSuccessMessage("Registration successful. Please verify your email.");

//       setTimeout(() => {
//         navigate("/verify-email");
//       }, 1200);
//     } catch (error) {
//       //console.log(error);
//       setError("Registration failed.");
//     } finally {
//       setLoading(false);
//     }
//   };

//   return (
//     <main className="page auth-page">
//       <section className="auth-card">
//         <p className="eyebrow">CREATE ACCOUNT</p>
//         <h1>Register</h1>
//         <p className="auth-text">
//           Create a user account to access TaskTracker features.
//         </p>

//         <form onSubmit={handleRegister} className="auth-form">
//           <div className="form-group">
//             <label>First name</label>
//             <input
//               value={firstName}
//               onChange={(e) => setFirstName(e.target.value)}
//               placeholder="First name"
//               required
//             />
//           </div>

//           <div className="form-group">
//             <label>Last name</label>
//             <input
//               value={lastName}
//               onChange={(e) => setLastName(e.target.value)}
//               placeholder="Last name"
//               required
//             />
//           </div>
//           <div className="form-group">
//             <label>Username</label>
//             <input
//               value={userName}
//               onChange={(e) => setUserName(e.target.value)}
//               placeholder="Username"
//               required
//             />
//           </div>
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
//             <label>Password</label>
//             <input
//               value={password}
//               onChange={(e) => setPassword(e.target.value)}
//               placeholder="Your password"
//               type="password"
//               required
//             />
//           </div>

//           {error && <p className="error-message">{error}</p>}
//           {successMessage && (
//             <p className="success-message">{successMessage}</p>
//           )}

//           <button className="primary-button" type="submit" disabled={loading}>
//             {loading ? "Creating account..." : "Register"}
//           </button>
//         </form>
//       </section>
//     </main>
//   );
// }

// export default RegisterPage;


import { useState } from "react";
import axios from "axios";
import { useNavigate } from "react-router-dom";
import { register } from "../api/authService";

function RegisterPage() {
  const navigate = useNavigate();

  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [userName, setUserName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [successMessage, setSuccessMessage] = useState("");

  const handleRegister = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    if (password.length < 12 || password.length > 128) {
      setError("Password must be between 12 and 128 characters.");
      return;
    }

    try {
      setLoading(true);
      setError("");
      setSuccessMessage("");

      const canonicalEmail = email.trim().toLowerCase();
      const displayUserName = userName.trim();

      const response = await register({
        firstName,
        lastName,
        email: canonicalEmail,
        userName: displayUserName,
        password,
      });

      if (response.code === "verification_delivery_failed") {
        navigate("/verify-email", {
          state: { email: canonicalEmail, deliveryFailed: true },
        });
        return;
      }

      setSuccessMessage("Registration successful. Please verify your email.");

      setTimeout(() => {
        navigate("/verify-email", { state: { email: canonicalEmail } });
      }, 1200);
    } catch (requestError: unknown) {
      const responseData = axios.isAxiosError<{
        code?: string;
        message?: string;
      }>(requestError)
        ? requestError.response?.data
        : undefined;

      if (responseData?.code === "verification_delivery_failed") {
        navigate("/verify-email", {
          state: { email: email.trim().toLowerCase(), deliveryFailed: true },
        });
        return;
      }

      const message = axios.isAxiosError<{ message?: string }>(requestError)
        ? requestError.response?.data?.message
        : undefined;

      setError(message ?? "Registration failed.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="page auth-page">
      <section className="auth-card register-card">
        <p className="eyebrow">CREATE ACCOUNT</p>

        <h1>Register</h1>

        <p className="auth-text">
          Create your TaskTracker account and start managing personal and shared
          tasks from one workspace.
        </p>

        <form onSubmit={handleRegister} className="auth-form">
          <div className="form-grid-two">
            <div className="form-group">
              <label>First name</label>
              <input
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
                placeholder="First name"
                required
              />
            </div>

            <div className="form-group">
              <label>Last name</label>
              <input
                value={lastName}
                onChange={(e) => setLastName(e.target.value)}
                placeholder="Last name"
                required
              />
            </div>
          </div>

          <div className="form-group">
            <label>Username</label>
            <input
              value={userName}
              onChange={(e) => setUserName(e.target.value)}
              placeholder="Choose a username"
              maxLength={50}
              required
            />
          </div>

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
            <label>Password</label>
            <input
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Create a password"
              type="password"
              minLength={12}
              maxLength={128}
              required
            />
          </div>

          {error && <p className="error-message">{error}</p>}

          {successMessage && (
            <p className="success-message">{successMessage}</p>
          )}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Creating account..." : "Create account"}
          </button>
        </form>

        <p className="auth-bottom-text">
          Already have an account? <button onClick={() => navigate("/login")}>Sign in</button>
        </p>
      </section>
    </main>
  );
}

export default RegisterPage;
