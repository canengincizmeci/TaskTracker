import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";

import "./styles/foundations.css";
import "./index.css";
import "./styles/shell.css";
import "./styles/dashboard.css";
import "./styles/task-detail.css";
import "./styles/task-creation.css";
import "./styles/inbox.css";
import "./styles/account.css";
import App from "./App.tsx";

import { AuthProvider } from "./context/AuthContext";



createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>

      
        <App />

      </AuthProvider>
    </BrowserRouter>
  </StrictMode>
);
