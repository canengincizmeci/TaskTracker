// import { useState } from "react";
// import { useLocation, useNavigate } from "react-router-dom";
// import { login } from "../api/authService";
// import { getRoleFromToken } from "../utils/jwtHelper";
// import { useAuth } from "../context/AuthContext";
// import toast from "react-hot-toast";

// function LoginPage() {
//   const navigate = useNavigate();
//   const location = useLocation();

//   console.log("Login location:", location);
//   console.log("Login state:", location.state);
//   console.log("Redirect from:", location.state?.from?.pathname);

//   const { loginToContext } = useAuth();

//   const [email, setEmail] = useState("");
//   const [password, setPassword] = useState("");

//   const [loading, setLoading] = useState(false);
//   const [error, setError] = useState("");

//   const handleLogin = async (e: React.FormEvent<HTMLFormElement>) => {
//     e.preventDefault();

//     try {
//       setLoading(true);
//       setError("");

//       const response = await login({
//         email,
//         password,
//       });

//       const token = response.data.accessToken.token;

//       loginToContext(
//         token,
//         response.data.refreshToken,
//         response.data.accessToken.expiration,
//       );

//       toast.success("Login successful");

//       const role = getRoleFromToken(token);

//       const from = location.state?.from?.pathname;

//       if (from) {
//         navigate(from, { replace: true });
//         return;
//       }

//       if (role === "Admin") {
//         navigate("/admin-dashboard");
//       } else {
//         navigate("/");
//       }
//     } catch (error) {
//       setError("Email or password is wrong.");
//       toast.error("Email or password is wrong.");
//     } finally {
//       setLoading(false);
//     }
//   };

//   return (
//     <main className="login-page">
//       <div className="login-bg-glow login-bg-glow-one"></div>
//       <div className="login-bg-glow login-bg-glow-two"></div>

//       <section className="login-shell">
//         <div className="login-brand-panel">
//           <div className="brand-badge">TaskTracker</div>

//           <h1>Manage work. Share tasks. Move faster.</h1>

//           <p>
//             A collaborative workspace for tracking requests, assigning people,
//             and keeping every task under control.
//           </p>

//           <div className="login-stats">
//             <div>
//               <strong>Private</strong>
//               <span>User based task ownership</span>
//             </div>

//             <div>
//               <strong>Shared</strong>
//               <span>Invite teammates to tasks</span>
//             </div>

//             <div>
//               <strong>Secure</strong>
//               <span>JWT protected workflow</span>
//             </div>
//           </div>
//         </div>

//         <section className="login-card">
//           <p className="eyebrow">TASKTRACKER ACCESS</p>

//           <h2>Welcome back</h2>

//           <p className="auth-text">
//             Sign in with your account. Admin and user accounts use the same
//             login page.
//           </p>

//           <form onSubmit={handleLogin} className="auth-form">
//             <div className="form-group">
//               <label>Email</label>

//               <input
//                 value={email}
//                 onChange={(e) => setEmail(e.target.value)}
//                 placeholder="your@email.com"
//                 type="email"
//                 required
//               />
//             </div>

//             <div className="form-group">
//               <label>Password</label>

//               <input
//                 value={password}
//                 onChange={(e) => setPassword(e.target.value)}
//                 placeholder="Your password"
//                 type="password"
//                 required
//               />
//             </div>

//             {error && <p className="error-message">{error}</p>}

//             <button className="primary-button" type="submit" disabled={loading}>
//               {loading ? "Signing in..." : "Sign in"}
//             </button>
//           </form>

//           <p className="login-footer-text">
//             Your tasks, shared workspaces and permissions are protected.
//           </p>
//         </section>
//       </section>
//     </main>
//   );
// }

// export default LoginPage;
import { useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { login } from "../api/authService";
import { getRoleFromToken } from "../utils/jwtHelper";
import { useAuth } from "../context/AuthContext";
import toast from "react-hot-toast";

function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();

  const { loginToContext } = useAuth();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleLogin = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    try {
      setLoading(true);
      setError("");

      const response = await login({
        email,
        password,
      });

      const token = response.data.accessToken.token;

      loginToContext(
        token,
        response.data.refreshToken,
        response.data.accessToken.expiration,
      );

      toast.success("Login successful");

      const role = getRoleFromToken(token);

      const searchParams = new URLSearchParams(location.search);
      const redirect = searchParams.get("redirect");

      if (redirect) {
        navigate(redirect, { replace: true });
        return;
      }

      if (role === "Admin") {
        navigate("/admin-dashboard", { replace: true });
      } else {
        navigate("/", { replace: true });
      }
    } catch (error) {
      setError("Email or password is wrong.");
      toast.error("Email or password is wrong.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="login-page">
      <div className="login-bg-glow login-bg-glow-one"></div>
      <div className="login-bg-glow login-bg-glow-two"></div>

      <section className="login-shell">
        <div className="login-brand-panel">
          <div className="brand-badge">TaskTracker</div>

          <h1>Manage work. Share tasks. Move faster.</h1>

          <p>
            A collaborative workspace for tracking requests, assigning people,
            and keeping every task under control.
          </p>

          <div className="login-stats">
            <div>
              <strong>Private</strong>
              <span>User based task ownership</span>
            </div>

            <div>
              <strong>Shared</strong>
              <span>Invite teammates to tasks</span>
            </div>

            <div>
              <strong>Secure</strong>
              <span>JWT protected workflow</span>
            </div>
          </div>
        </div>

        <section className="login-card">
          <p className="eyebrow">TASKTRACKER ACCESS</p>

          <h2>Welcome back</h2>

          <p className="auth-text">
            Sign in with your account. Admin and user accounts use the same
            login page.
          </p>

          <form onSubmit={handleLogin} className="auth-form">
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
                placeholder="Your password"
                type="password"
                required
              />
            </div>

            <Link to="/forgot-password">Forgot password?</Link>

            {error && <p className="error-message">{error}</p>}

            <button className="primary-button" type="submit" disabled={loading}>
              {loading ? "Signing in..." : "Sign in"}
            </button>
          </form>

          <p className="login-footer-text">
            Your tasks, shared workspaces and permissions are protected.
          </p>
        </section>
      </section>
    </main>
  );
}

export default LoginPage;
