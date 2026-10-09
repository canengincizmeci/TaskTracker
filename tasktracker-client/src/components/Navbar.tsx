import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

function Navbar() {
  const [menuOpen, setMenuOpen] = useState(false);

  useEffect(() => {
    if (!menuOpen) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setMenuOpen(false);
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [menuOpen]);

  return (
    <header className="navbar">
      <Link to="/" className="navbar-logo" onClick={() => setMenuOpen(false)}>
        TaskTracker
      </Link>

      <button type="button" className="navbar-menu-button" aria-expanded={menuOpen}
        aria-controls="primary-navigation" onClick={() => setMenuOpen((current) => !current)}>
        <svg className="navigation-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M4 7h16M4 12h16M4 17h16" /></svg>
        Menu
      </button>

      <nav id="primary-navigation" className={`navbar-links${menuOpen ? " navbar-links--open" : ""}`}
        aria-label="Primary navigation">
        <Link to="/" onClick={() => setMenuOpen(false)}>Home</Link>
        <Link to="/login" onClick={() => setMenuOpen(false)}>Login</Link>
        <Link to="/register" onClick={() => setMenuOpen(false)}>Register</Link>
      </nav>
    </header>
  );
}

export default Navbar;
