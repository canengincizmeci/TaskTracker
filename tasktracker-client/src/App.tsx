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
          duration: 3200,
          style: {
            background: "var(--color-surface-elevated)",
            color: "var(--color-text)",
            border: "1px solid var(--color-border-strong)",
            borderRadius: "var(--radius-md)",
            boxShadow: "var(--shadow-md)",
            fontSize: "0.8125rem",
            fontWeight: 550,
          },
        }}
      />
    </>
  );
}

export default App;
