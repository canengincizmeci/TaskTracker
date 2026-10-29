import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link, NavLink, useLocation, useNavigate } from "react-router-dom";
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

type ShellIconName =
  | "work"
  | "workspace"
  | "inbox"
  | "create"
  | "admin"
  | "profile"
  | "security"
  | "logout"
  | "menu"
  | "close";

function ShellIcon({ name }: { name: ShellIconName }) {
  const paths: Record<ShellIconName, ReactNode> = {
    work: <><path d="M4 7.5h16v11H4z" /><path d="M8 7.5V5h8v2.5M4 12h16M10 12v2h4v-2" /></>,
    workspace: <><path d="M4 5h7v6H4zM13 5h7v6h-7zM4 13h7v6H4zM13 13h7v6h-7z" /></>,
    inbox: <><path d="M4 5h16v14H4z" /><path d="M4 14h4l2 2h4l2-2h4" /></>,
    create: <path d="M12 5v14M5 12h14" />,
    admin: <><path d="M12 3 5 6v5c0 4.6 2.9 8 7 10 4.1-2 7-5.4 7-10V6z" /><path d="m9 12 2 2 4-4" /></>,
    profile: <><circle cx="12" cy="8" r="3" /><path d="M5 20c.6-4 3-6 7-6s6.4 2 7 6" /></>,
    security: <><rect x="5" y="10" width="14" height="10" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3" /></>,
    logout: <><path d="M10 5H5v14h5M14 8l4 4-4 4M9 12h9" /></>,
    menu: <path d="M4 7h16M4 12h16M4 17h16" />,
    close: <path d="m6 6 12 12M18 6 6 18" />,
  };

  return <svg className="navigation-icon" aria-hidden="true" viewBox="0 0 24 24">{paths[name]}</svg>;
}

function UnreadBadge({ count }: { count: number }) {
  if (count <= 0) return null;
  return <span className="app-shell__unread" aria-hidden="true">{count > 99 ? "99+" : count}</span>;
}

function pageTitle(pathname: string) {
  if (pathname === "/dashboard") return "My Work";
  if (pathname === "/workspaces") return "Workspaces";
  if (pathname.startsWith("/workspaces/")) return "Workspace";
  if (pathname === "/notifications") return "Inbox";
  if (pathname === "/tasks/create-task") return "Create task";
  if (pathname.startsWith("/tasks/task-detail/")) return "Task details";
  if (pathname.startsWith("/tasks/task-share/")) return "Share task";
  if (pathname.startsWith("/tasks/invitations")) return "Task invitations";
  if (pathname === "/profile") return "Profile";
  if (pathname === "/settings/security") return "Security";
  if (pathname === "/admin-dashboard") return "Administration";
  return "TaskTracker";
}

function ShellNavLink({ to, icon, label, count, end = false, active = false, onClick }: {
  to: string;
  icon: ShellIconName;
  label: string;
  count?: number;
  end?: boolean;
  active?: boolean;
  onClick?: () => void;
}) {
  return <NavLink to={to} end={end} onClick={onClick}
    aria-label={count ? `${label} (${count} unread)` : label}
    aria-current={active ? "page" : undefined}
    className={({ isActive }) => `app-shell__nav-link${isActive || active ? " app-shell__nav-link--active" : ""}`}>
    <ShellIcon name={icon} />
    <span>{label}</span>
    {count !== undefined && <UnreadBadge count={count} />}
  </NavLink>;
}

export default function AppShell({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAuthenticated, token, user, logout } = useAuth();
  const [unread, setUnread] = useState<AccountUnreadState>({ accountToken: null, count: 0 });
  const [accountOpen, setAccountOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const accountButtonRef = useRef<HTMLButtonElement>(null);
  const mobileButtonRef = useRef<HTMLButtonElement>(null);
  const mobileCloseButtonRef = useRef<HTMLButtonElement>(null);

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
          if (allMarkedRead) return;

          notifications
            .filter((notification) =>
              notification.isRead === false && !locallyReadIds.has(notification.id))
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

  useEffect(() => {
    if (!accountOpen && !mobileOpen) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      if (mobileOpen) {
        setMobileOpen(false);
        mobileButtonRef.current?.focus();
      } else {
        setAccountOpen(false);
        accountButtonRef.current?.focus();
      }
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [accountOpen, mobileOpen]);

  const closeNavigation = () => {
    setAccountOpen(false);
    setMobileOpen(false);
  };

  const toggleMobileNavigation = () => {
    if (mobileOpen) {
      setMobileOpen(false);
      return;
    }
    setMobileOpen(true);
    window.requestAnimationFrame(() => mobileCloseButtonRef.current?.focus());
  };

  const handleLogout = () => {
    closeNavigation();
    setUnread({ accountToken: null, count: 0 });
    logout();
    navigate("/login");
  };

  const displayedUnreadCount = isAuthenticated && unread.accountToken === token ? unread.count : 0;
  const accountLabel = user?.name ?? user?.email ?? "Signed in";
  const accountInitial = accountLabel.trim().charAt(0).toUpperCase() || "A";
  const pathname = location.pathname;
  const myWorkActive = pathname === "/dashboard" ||
    pathname === "/tasks/user-tasks" ||
    pathname === "/tasks/assigned-to-me" ||
    pathname === "/tasks/awaiting-review" ||
    pathname === "/tasks/shared-tasks" ||
    pathname.startsWith("/tasks/task-detail/") ||
    pathname.startsWith("/tasks/task-share/");
  const workspacesActive = pathname === "/workspaces" || pathname.startsWith("/workspaces/");
  const inboxActive = pathname === "/notifications" || pathname === "/tasks/invitations" ||
    pathname.startsWith("/tasks/invitations/");

  const accountLinks = <>
    {user?.role === "Admin" && <ShellNavLink to="/admin-dashboard" icon="admin" label="Administration" end onClick={closeNavigation} />}
    <ShellNavLink to="/profile" icon="profile" label="Profile" end onClick={closeNavigation} />
    <ShellNavLink to="/settings/security" icon="security" label="Security" end onClick={closeNavigation} />
    <button type="button" className="app-shell__nav-link app-shell__logout" onClick={handleLogout}>
      <ShellIcon name="logout" /><span>Logout</span>
    </button>
  </>;

  return <div className={`app-shell${user?.role === "User" ? " app-shell--user" : ""}`}>
    <aside className="app-shell__sidebar" aria-label="Application sidebar">
      <Link className="app-shell__brand" to={user?.role === "Admin" ? "/admin-dashboard" : "/dashboard"}
        aria-label="TaskTracker">
        <span className="app-shell__brand-mark" aria-hidden="true">T</span>
        <span>TaskTracker</span>
      </Link>

      {user?.role === "User" && <Link className="app-shell__create" to="/tasks/create-task" aria-label="Create task">
        <ShellIcon name="create" /><span>Create task</span>
      </Link>}

      <nav className="app-shell__primary-nav" aria-label="Primary navigation">
        {user?.role === "User" && <>
          <ShellNavLink to="/dashboard" icon="work" label="My Work" end active={myWorkActive} />
          <ShellNavLink to="/workspaces" icon="workspace" label="Workspaces" active={workspacesActive} />
        </>}
        <ShellNavLink to="/notifications" icon="inbox" label="Inbox" count={displayedUnreadCount}
          end active={inboxActive} />
        {user?.role === "Admin" && <ShellNavLink to="/admin-dashboard" icon="admin" label="Administration" end />}
      </nav>

      <div className="app-shell__account">
        <button ref={accountButtonRef} type="button" className="app-shell__account-button"
          aria-expanded={accountOpen} aria-controls="shell-account-navigation"
          aria-label={accountOpen ? "Close account navigation" : "Open account navigation"}
          onClick={() => setAccountOpen((current) => !current)}>
          <span className="app-shell__avatar" aria-hidden="true">{accountInitial}</span>
          <span className="app-shell__account-copy"><strong>{accountLabel}</strong><small>{user?.role ?? "Account"}</small></span>
          <svg className="app-shell__chevron" aria-hidden="true" viewBox="0 0 24 24"><path d="m8 10 4 4 4-4" /></svg>
        </button>
        {accountOpen && <nav id="shell-account-navigation" className="app-shell__account-popover" aria-label="Account navigation">
          {accountLinks}
        </nav>}
      </div>
    </aside>

    <div className="app-shell__main">
      <header className="app-shell__topbar">
        <button ref={mobileButtonRef} type="button" className="app-shell__mobile-trigger"
          aria-expanded={mobileOpen} aria-controls="shell-mobile-panel"
          aria-label={mobileOpen ? "Close account navigation" : "Open account navigation"}
          onClick={toggleMobileNavigation}>
          <ShellIcon name={mobileOpen ? "close" : "menu"} />
        </button>
        <div><span>TaskTracker</span><strong>{pageTitle(location.pathname)}</strong></div>
        <Link className="app-shell__topbar-inbox" to="/notifications"
          aria-label={`Inbox${displayedUnreadCount > 0 ? ` (${displayedUnreadCount} unread)` : ""}`}>
          <ShellIcon name="inbox" /><UnreadBadge count={displayedUnreadCount} />
        </Link>
      </header>

      {mobileOpen && <>
        <button type="button" className="app-shell__scrim" aria-label="Close account navigation"
          onClick={() => {
            setMobileOpen(false);
            mobileButtonRef.current?.focus();
          }} />
        <aside id="shell-mobile-panel" className="app-shell__mobile-panel" aria-label="Account navigation">
          <header><span className="app-shell__avatar" aria-hidden="true">{accountInitial}</span>
            <div><strong>{accountLabel}</strong><small>{user?.role ?? "Account"}</small></div></header>
          <button ref={mobileCloseButtonRef} type="button" className="app-shell__mobile-panel-close"
            aria-label="Close account navigation" onClick={() => {
              setMobileOpen(false);
              mobileButtonRef.current?.focus();
            }}><ShellIcon name="close" /></button>
          <nav aria-label="Account and administration">{accountLinks}</nav>
        </aside>
      </>}

      <div className="app-shell__content">{children}</div>
    </div>

    {user?.role === "User" && <nav className="app-shell__mobile-nav" aria-label="Mobile primary navigation">
      <ShellNavLink to="/dashboard" icon="work" label="My Work" end active={myWorkActive} />
      <ShellNavLink to="/workspaces" icon="workspace" label="Workspaces" active={workspacesActive} />
      <ShellNavLink to="/tasks/create-task" icon="create" label="Create" end />
      <ShellNavLink to="/notifications" icon="inbox" label="Inbox" count={displayedUnreadCount}
        end active={inboxActive} />
    </nav>}
  </div>;
}
