import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { getUserNotifications } from "../api/notificationService";
import { useAuth } from "../context/AuthContext";
import { notificationHubConnection } from "../services/signalRService";
import type { Notification } from "../types/notification";

function Navbar() {
  const navigate = useNavigate();
  const { isAuthenticated, user, logout } = useAuth();
  const [unreadCount, setUnreadCount] = useState(0);

  useEffect(() => {
    if (!isAuthenticated) {
      setUnreadCount(0);
      return;
    }

    let isActive = true;
    const knownUnreadIds = new Set<number>();

    const handleNotification = (notification: Notification) => {
      if (!notification.isRead && !knownUnreadIds.has(notification.id)) {
        knownUnreadIds.add(notification.id);
        setUnreadCount((currentCount) => currentCount + 1);
      }
    };

    notificationHubConnection.on("ReceiveNotification", handleNotification);

    const loadUnreadCount = async () => {
      try {
        const notifications = await getUserNotifications();

        if (isActive) {
          notifications
            .filter((notification) => notification.isRead === false)
            .forEach((notification) => knownUnreadIds.add(notification.id));

          setUnreadCount(knownUnreadIds.size);
        }
      } catch (error) {
        console.error("Failed to load unread notification count:", error);
      }
    };

    void loadUnreadCount();

    return () => {
      isActive = false;
      notificationHubConnection.off("ReceiveNotification", handleNotification);
    };
  }, [isAuthenticated]);

  const handleLogout = () => {
    logout();
    navigate("/login");
  };

  return (
    <header className="navbar">
      <Link to="/" className="navbar-logo">
        TaskTracker
      </Link>

      <nav className="navbar-links">
        <Link to="/">Home</Link>

        {isAuthenticated && (
          <Link to="/tasks/create-task" className="create-task-link">
            + Create Task
          </Link>
        )}

        {user?.role === "Admin" && (
          <Link to="/admin-dashboard">Dashboard</Link>
        )}

        {!isAuthenticated ? (
          <>
            <Link to="/login">Login</Link>
            <Link to="/register">Register</Link>
          </>
        ) : (
          <>
            <Link
              to="/notifications"
              aria-label={`Notifications${unreadCount > 0 ? ` (${unreadCount} unread)` : ""}`}
            >
              <span aria-hidden="true">🔔</span> Notifications
              {unreadCount > 0 && <span> ({unreadCount})</span>}
            </Link>
            <Link to="/profile">Profile</Link>

            <span className="navbar-user">
              {user?.name ? `Hello, ${user.name}` : "Account"}
            </span>

            <button className="navbar-logout" onClick={handleLogout}>
              Logout
            </button>
          </>
        )}
      </nav>
    </header>
  );
}

export default Navbar;
