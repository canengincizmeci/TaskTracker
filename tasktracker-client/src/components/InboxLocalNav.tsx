import { NavLink } from "react-router-dom";

export default function InboxLocalNav() {
  return (
    <nav className="inbox-local-nav" aria-label="Inbox sections">
      <NavLink to="/notifications" end className={({ isActive }) => isActive ? "inbox-local-nav__link inbox-local-nav__link--active" : "inbox-local-nav__link"}>
        Notifications
      </NavLink>
      <NavLink to="/tasks/invitations" className={({ isActive }) => isActive ? "inbox-local-nav__link inbox-local-nav__link--active" : "inbox-local-nav__link"}>
        Task invitations
      </NavLink>
    </nav>
  );
}
