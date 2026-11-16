import AppRouter from "./router/AppRouter";
import Navbar from "./components/Navbar";
import AppShell from "./components/AppShell";
import { useAuth } from "./context/AuthContext";
import { Toaster } from "react-hot-toast";
import { useLocation } from "react-router-dom";

const standalonePublicRoutes = new Set([
  "/login",
  "/register",
  "/verify-email",
  "/forgot-password",
  "/verify-password-reset",
  "/reset-password",
]);

function App() {
  const { isAuthenticated } = useAuth();
  const location = useLocation();
  const normalizedPathname = location.pathname.replace(/\/+$/, "") || "/";
  const showPublicNavbar = !standalonePublicRoutes.has(normalizedPathname);

  return (
    <>
      {isAuthenticated ? (
        <AppShell>
          <AppRouter />
        </AppShell>
      ) : (
        <>
          {showPublicNavbar && <Navbar />}
          <AppRouter />
        </>
      )}

      <Toaster
        position="top-right"
        toastOptions={{
          duration: 3000,
          style: {
            background: "#111827",
            color: "#fff",
            border: "1px solid rgba(255,255,255,0.08)",
          },
        }}
      />
    </>
  );
}

export default App;
