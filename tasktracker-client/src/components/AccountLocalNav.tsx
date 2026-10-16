import { NavLink } from "react-router-dom";

export default function AccountLocalNav() {
  return (
    <nav className="account-local-nav" aria-label="Account settings">
      <NavLink
        to="/profile"
        end
        className={({ isActive }) =>
          isActive
            ? "account-local-nav__link account-local-nav__link--active"
            : "account-local-nav__link"
        }
      >
        Profile
      </NavLink>
      <NavLink
        to="/settings/security"
        end
        className={({ isActive }) =>
          isActive
            ? "account-local-nav__link account-local-nav__link--active"
            : "account-local-nav__link"
        }
      >
        Security
      </NavLink>
    </nav>
  );
}
