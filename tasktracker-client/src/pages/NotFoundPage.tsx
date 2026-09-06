import { Link } from "react-router-dom";

function NotFoundPage() {
  return (
    <main className="page auth-page">
      <section className="auth-card">
        <h1>404</h1>
        <p className="auth-text">The page you are looking for could not be found.</p>
        <Link to="/" className="primary-button">
          Back to TaskTracker
        </Link>
      </section>
    </main>
  );
}

export default NotFoundPage;
