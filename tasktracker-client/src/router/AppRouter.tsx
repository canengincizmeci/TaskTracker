import { Routes, Route } from "react-router-dom";
import HomePage from "../pages/HomePage";
import TaskDetailPage from "../pages/TaskDetailPage";
import LoginPage from "../pages/LoginPage";
import RegisterPage from "../pages/RegisterPage";
import VerifyEmailPage from "../pages/VerifyEmailPage";
import AdminDashboardPage from "../pages/AdminDashboardPage";
import ProfilePage from "../pages/ProfilePage";
import ProtectedRoute from "./ProtectedRoute";
import CreateTaskPage from "../pages/CreateTaskPage";
import UserTasksPage from "../pages/UserTasksPage";
import TaskSharePage from "../pages/TaskSharePage";
import NotificationsPage from "../pages/NotificationsPage";
import TaskInvitationsPage from "../pages/TaskInvitationsPage";
import SharedTasksPage from "../pages/SharedTasksPage";
import TaskInvitationDetailPage from "../pages/TaskInvitationDetailPage";

function AppRouter() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />

      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route
        path="/profile"
        element={
          <ProtectedRoute>
            <ProfilePage />
          </ProtectedRoute>
        }
      />

      <Route
        path="/admin-dashboard"
        element={
          <ProtectedRoute requiredRole="Admin">
            <AdminDashboardPage />
          </ProtectedRoute>
        }
      />

      <Route
        path="/tasks/create-task"
        element={
          <ProtectedRoute>
            <CreateTaskPage />
          </ProtectedRoute>
        }
      />

      <Route
        path="/tasks/user-tasks"
        element={
          <ProtectedRoute>
            <UserTasksPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/tasks/task-detail/:taskId"
        element={
          <ProtectedRoute>
            <TaskDetailPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/tasks/task-share/:taskId"
        element={
          <ProtectedRoute>
            <TaskSharePage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/notifications"
        element={
          <ProtectedRoute>
            <NotificationsPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/tasks/invitations"
        element={
          <ProtectedRoute>
            <TaskInvitationsPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/tasks/invitations/:invitationId"
        element={
          <ProtectedRoute>
            <TaskInvitationDetailPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/tasks/shared-tasks"
        element={
          <ProtectedRoute>
            <SharedTasksPage />
          </ProtectedRoute>
        }
      />
     
    </Routes>
  );
}

export default AppRouter;
