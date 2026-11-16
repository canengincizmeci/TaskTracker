import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

function NotFoundPage() {
  const { isAuthenticated, user } = useAuth();
  const destination = !isAuthenticated ? "/" : user?.role === "Admin" ? "/admin-dashboard" : "/dashboard";
  const destinationLabel = isAuthenticated ? "Go to dashboard" : "Back to home";

  return (
    <main className="page not-found-page">
      <section className="not-found-panel" aria-labelledby="not-found-title">
        <Link className="not-found-brand" to={destination} aria-label="TaskTracker">
          <span aria-hidden="true">T</span><strong>TaskTracker</strong>
        </Link>
        <p className="eyebrow">ERROR 404</p>
        <h1 id="not-found-title">This page is out of view.</h1>
        <p>The address may be incorrect, or the page may have moved. Your account and tasks have not been changed.</p>
        <Link to={destination} className="primary-button">{destinationLabel}</Link>
      </section>
      <div className="not-found-code" aria-hidden="true">404</div>
    </main>
  );
}

export default NotFoundPage;
