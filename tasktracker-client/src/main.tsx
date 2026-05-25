import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";

import "./index.css";
import App from "./App.tsx";

import { AuthProvider } from "./context/AuthContext.tsx";

import { Toaster } from "react-hot-toast";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>

        <Toaster
          position="top-right"
          reverseOrder={false}
        />

        <App />

      </AuthProvider>
    </BrowserRouter>
  </StrictMode>
);