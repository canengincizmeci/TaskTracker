import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

function Navbar() {
  const navigate = useNavigate();
  const { isAuthenticated, user, logout } = useAuth();

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
          <Link to="/tasks/create" className="create-task-link">
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