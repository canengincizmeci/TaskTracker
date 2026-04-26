import { BrowserRouter, Routes, Route } from "react-router-dom";
import HomePage from "../pages/HomePage";
import TaskDetailPage from "../pages/TaskDetailPage";
import AdminLoginPage from "../pages/AdminLoginPage";
import AdminVerifyOtpPage from "../pages/AdminVerifyOtpPage";
import AdminDashboardPage from "../pages/AdminDashboardPage";

function AppRouter() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/task/:id" element={<TaskDetailPage />} />
        <Route path="/secret-admin-entry" element={<AdminLoginPage />} />
        <Route
  path="/secret-admin-entry/verify"
  element={<AdminVerifyOtpPage />}
/>
<Route path="/admin-dashboard" element={<AdminDashboardPage />} />
      </Routes>
    </BrowserRouter>
  );
}

export default AppRouter;