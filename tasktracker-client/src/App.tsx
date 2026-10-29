import AppRouter from "./router/AppRouter";
import Navbar from "./components/Navbar";
import AppShell from "./components/AppShell";
import { useAuth } from "./context/AuthContext";
import { Toaster } from "react-hot-toast";

function App() {
  const { isAuthenticated } = useAuth();

  return (
    <>
      {isAuthenticated ? (
        <AppShell>
          <AppRouter />
        </AppShell>
      ) : (
        <>
          <Navbar />
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
