import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { getUserNotifications } from "../api/notificationService";
import { useAuth } from "../context/AuthContext";
import { notificationHubConnection } from "../services/signalRService";
import type { Notification } from "../types/notification";

const UNREAD_COUNT_EVENT = "notification-unread-count-change";

type UnreadCountEventDetail = {
  notificationId?: number;
  reset?: boolean;
};

type AccountUnreadState = {
  accountToken: string | null;
  count: number;
};

function Navbar() {
  const navigate = useNavigate();
  const { isAuthenticated, token, user, logout } = useAuth();
  const [unread, setUnread] = useState<AccountUnreadState>({ accountToken: null, count: 0 });
  const [menuOpen, setMenuOpen] = useState(false);
  const [accountOpen, setAccountOpen] = useState(false);

  useEffect(() => {
    if (!isAuthenticated || !token) {
      const reset = window.setTimeout(() => setUnread({ accountToken: null, count: 0 }), 0);
      return () => window.clearTimeout(reset);
    }

    let isActive = true;
    let allMarkedRead = false;
    const knownUnreadIds = new Set<number>();
    const locallyReadIds = new Set<number>();

    const handleNotification = (notification: Notification) => {
      if (!notification.isRead && !knownUnreadIds.has(notification.id)) {
        knownUnreadIds.add(notification.id);
        setUnread((current) => ({ accountToken: token,
          count: (current.accountToken === token ? current.count : 0) + 1 }));
      }
    };

    const handleUnreadCountChange = (event: Event) => {
      const { notificationId, reset } = (
        event as CustomEvent<UnreadCountEventDetail>
      ).detail;

      if (reset) {
        allMarkedRead = true;
        knownUnreadIds.clear();
        locallyReadIds.clear();
        setUnread({ accountToken: token, count: 0 });
        return;
      }

      if (notificationId !== undefined && !locallyReadIds.has(notificationId)) {
        locallyReadIds.add(notificationId);
        knownUnreadIds.delete(notificationId);
        setUnread((current) => ({ accountToken: token,
          count: Math.max(0, (current.accountToken === token ? current.count : 0) - 1) }));
      }
    };

    notificationHubConnection.on("ReceiveNotification", handleNotification);
    window.addEventListener(UNREAD_COUNT_EVENT, handleUnreadCountChange);

    const loadUnreadCount = async () => {
      try {
        const notifications = await getUserNotifications();

        if (isActive) {
          if (allMarkedRead) {
            return;
          }

          notifications
            .filter(
              (notification) =>
                notification.isRead === false &&
                !locallyReadIds.has(notification.id)
            )
            .forEach((notification) => knownUnreadIds.add(notification.id));

          setUnread({ accountToken: token, count: knownUnreadIds.size });
        }
      } catch (error) {
        console.error("Failed to load unread notification count:", error);
      }
    };

    void loadUnreadCount();

    return () => {
      isActive = false;
      notificationHubConnection.off("ReceiveNotification", handleNotification);
      window.removeEventListener(UNREAD_COUNT_EVENT, handleUnreadCountChange);
    };
  }, [isAuthenticated, token]);

  const handleLogout = () => {
    setMenuOpen(false);
    setAccountOpen(false);
    setUnread({ accountToken: null, count: 0 });
    logout();
    navigate("/login");
  };

  const closeMenus = () => {
    setMenuOpen(false);
    setAccountOpen(false);
  };

  const displayedUnreadCount = isAuthenticated && unread.accountToken === token ? unread.count : 0;

  return (
    <header className="navbar">
      <Link to={isAuthenticated && user?.role === "User" ? "/dashboard" : "/"}
        className="navbar-logo" onClick={closeMenus}>
        TaskTracker
      </Link>

      <button type="button" className="navbar-menu-button" aria-expanded={menuOpen}
        aria-controls="primary-navigation" onClick={() => setMenuOpen((current) => !current)}>
        <span aria-hidden="true">☰</span> Menu
      </button>

      <nav id="primary-navigation" className={`navbar-links${menuOpen ? " navbar-links--open" : ""}`}
        aria-label="Primary navigation">
        {!isAuthenticated ? (
          <>
            <Link to="/" onClick={closeMenus}>Home</Link>
            <Link to="/login" onClick={closeMenus}>Login</Link>
            <Link to="/register" onClick={closeMenus}>Register</Link>
          </>
        ) : (
          <>
            {user?.role === "User" && (
              <div className="navbar-primary-links">
                <Link to="/dashboard" onClick={closeMenus}>My Work</Link>
                <Link to="/workspaces" onClick={closeMenus}>Workspaces</Link>
                <Link to="/tasks/create-task" className="create-task-link" onClick={closeMenus}>Create task</Link>
              </div>
            )}

            <Link className="navbar-inbox" to="/notifications" onClick={closeMenus}
              aria-label={`Inbox${displayedUnreadCount > 0 ? ` (${displayedUnreadCount} unread)` : ""}`}>
              Inbox
              {displayedUnreadCount > 0 && <span className="navbar-unread-count">{displayedUnreadCount}</span>}
            </Link>

            <div className="navbar-account">
              <button type="button" className="navbar-account-button" aria-expanded={accountOpen}
                aria-controls="account-navigation" onClick={() => setAccountOpen((current) => !current)}>
                Account <span aria-hidden="true">▾</span>
              </button>
              {accountOpen && <div id="account-navigation" className="navbar-account-menu">
                <span>{user?.name ?? user?.email ?? "Signed in"}</span>
                {user?.role === "Admin" && <Link to="/admin-dashboard" onClick={closeMenus}>Admin dashboard</Link>}
                <Link to="/profile" onClick={closeMenus}>Profile</Link>
                <Link to="/settings/security" onClick={closeMenus}>Security</Link>
                <button type="button" className="navbar-logout" onClick={handleLogout}>Logout</button>
              </div>}
            </div>
          </>
        )}
      </nav>
    </header>
  );
}

export default Navbar;
