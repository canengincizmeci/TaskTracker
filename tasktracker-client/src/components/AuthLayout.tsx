import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import ThemeToggle from "./ThemeToggle";

type AuthLayoutProps = {
  eyebrow: string;
  title: string;
  description: string;
  children: ReactNode;
  footer?: ReactNode;
};

export default function AuthLayout({ eyebrow, title, description, children, footer }: AuthLayoutProps) {
  return <main className="public-auth-page">
    <section className="public-auth-shell">
      <aside className="public-auth-brand" aria-label="About TaskTracker">
        <Link className="public-auth-logo" to="/" aria-label="TaskTracker home">
          <span aria-hidden="true">T</span><strong>TaskTracker</strong>
        </Link>
        <div className="public-auth-brand__message">
          <p className="public-auth-brand__kicker">A calmer way to move work forward</p>
          <h2>Keep ownership clear.<br />Keep momentum visible.</h2>
          <p>Organize personal work, collaborate in workspaces, and act on the next task that matters.</p>
        </div>
        <div className="public-auth-signals" aria-label="Product capabilities">
          <div><span aria-hidden="true" /><strong>Focused work</strong><small>Priorities and next actions in one view</small></div>
          <div><span aria-hidden="true" /><strong>Clear ownership</strong><small>Owners, assignees, and access stay visible</small></div>
          <div><span aria-hidden="true" /><strong>Shared context</strong><small>Discussion and history travel with the task</small></div>
        </div>
      </aside>

      <div className="public-auth-workspace">
        <div className="public-auth-utility">
          <Link className="public-auth-mobile-brand" to="/" aria-label="TaskTracker home">
            <span>T</span><strong>TaskTracker</strong>
          </Link>
          <ThemeToggle compact className="public-auth-theme-toggle" />
        </div>
        <section className="public-auth-panel" aria-labelledby="auth-page-title">
          <header className="public-auth-panel__header">
            <p className="public-auth-eyebrow">{eyebrow}</p>
            <h1 id="auth-page-title">{title}</h1>
            <p>{description}</p>
          </header>
          {children}
          {footer && <footer className="public-auth-panel__footer">{footer}</footer>}
        </section>
        <p className="public-auth-legal">Secure account access for TaskTracker.</p>
      </div>
    </section>
  </main>;
}
