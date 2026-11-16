import { Link } from "react-router-dom";

const previewTasks = [
  { title: "Finalize launch checklist", meta: "Product · Due today", status: "In progress", tone: "active", action: "Open task" },
  { title: "Review onboarding copy", meta: "Content · High priority", status: "In review", tone: "review", action: "Review" },
  { title: "Confirm workspace access", meta: "Operations · Assigned to you", status: "Not started", tone: "pending", action: "Start task" },
];

function ProductPreview() {
  return <div className="landing-preview" aria-label="Illustrative TaskTracker dashboard preview">
    <header className="landing-preview__topbar">
      <div><span className="landing-preview__mark">T</span><strong>TaskTracker</strong></div>
      <span className="landing-preview__label">Illustrative product view</span>
    </header>
    <div className="landing-preview__body">
      <section className="landing-preview__work" aria-label="Illustrative task list">
        <header><div><small>MY WORK</small><h2>What needs attention</h2></div><span>＋ New task</span></header>
        <div className="landing-preview__scopes"><strong>All work</strong><span>Owned</span><span>Assigned</span><span>Shared</span></div>
        <div className="landing-preview__group-heading"><span>Needs your attention</span><strong>3</strong></div>
        <div className="landing-preview__tasks">
          {previewTasks.map((task) => <article className="landing-preview-task" key={task.title}>
            <span className={`landing-preview-task__signal landing-preview-task__signal--${task.tone}`} aria-hidden="true" />
            <div><h3>{task.title}</h3><p>{task.meta}</p></div>
            <span className={`landing-preview-task__status landing-preview-task__status--${task.tone}`}>{task.status}</span>
            <strong>{task.action} <span aria-hidden="true">→</span></strong>
          </article>)}
        </div>
      </section>
      <aside className="landing-preview__pulse" aria-label="Illustrative work summary">
        <header><div><small>WORK SIGNALS</small><h2>Pulse</h2></div><span aria-hidden="true">↻</span></header>
        <div className="landing-pulse-row"><span /><div><strong>2</strong><small>Assigned to me</small></div></div>
        <div className="landing-pulse-row landing-pulse-row--warm"><span /><div><strong>1</strong><small>Awaiting review</small></div></div>
        <div className="landing-pulse-row landing-pulse-row--clear"><span /><div><strong>0</strong><small>Overdue</small></div></div>
        <div className="landing-preview__workspace"><small>WORKSPACE</small><strong>Product launch</strong><span>Tasks and people in one shared space</span></div>
      </aside>
    </div>
  </div>;
}

function HomePage() {
  return <main className="home-landing-page">
    <section className="landing-hero" aria-labelledby="landing-title">
      <div className="landing-hero__copy">
        <p className="landing-kicker"><span aria-hidden="true" /> Collaborative task management</p>
        <h1 id="landing-title">Know what needs action. Keep ownership clear.</h1>
        <p>TaskTracker brings personal tasks, shared workspaces, responsibility, due dates, and task history into one focused workflow.</p>
        <div className="landing-actions">
          <Link className="landing-button landing-button--primary" to="/register">Create account <span aria-hidden="true">→</span></Link>
          <Link className="landing-button landing-button--secondary" to="/login">Sign in</Link>
        </div>
        <div className="landing-hero__capabilities" aria-label="Core capabilities">
          <span>Personal work</span><span>Shared workspaces</span><span>Clear responsibility</span>
        </div>
      </div>
      <ProductPreview />
    </section>

    <section className="landing-value" aria-labelledby="landing-value-title">
      <div className="landing-value__intro">
        <p className="landing-section-kicker">Built around the work</p>
        <h2 id="landing-value-title">Less status hunting. More forward motion.</h2>
        <p>Every view keeps the task, the responsible people, and the next permitted action close together.</p>
      </div>
      <div className="landing-value__list">
        <article><span>01</span><div><h3>Focus your day</h3><p>See assigned work, reviews, overdue tasks, and invitations without turning your dashboard into a spreadsheet.</p></div></article>
        <article><span>02</span><div><h3>Move work through its lifecycle</h3><p>Start, submit, review, complete, or reopen work through actions that respect each person’s role.</p></div></article>
        <article><span>03</span><div><h3>Keep context attached</h3><p>Discussion, submissions, decisions, and task activity remain available alongside the work they describe.</p></div></article>
      </div>
    </section>

    <section className="landing-ownership" aria-labelledby="landing-ownership-title">
      <div className="landing-ownership__copy">
        <p className="landing-section-kicker">Collaboration without ambiguity</p>
        <h2 id="landing-ownership-title">One task. Clear responsibility.</h2>
        <p>Create personal work or bring tasks into a workspace. Owners retain control, assignees know what to do next, and collaborators see only what their access permits.</p>
        <Link to="/register">Create your workspace <span aria-hidden="true">→</span></Link>
      </div>
      <div className="landing-responsibility" aria-label="Illustration of task responsibility">
        <header><small>TASK RESPONSIBILITY</small><strong>Prepare release notes</strong><span>In progress</span></header>
        <div><span className="landing-avatar">OW</span><p><small>Owner</small><strong>Sets direction and reviews work</strong></p></div>
        <div><span className="landing-avatar landing-avatar--accent">AS</span><p><small>Assignee</small><strong>Moves the task to its next state</strong></p></div>
        <div><span className="landing-avatar landing-avatar--soft">CO</span><p><small>Collaborator</small><strong>Contributes with explicit access</strong></p></div>
        <footer><span>Priority · High</span><span>Due · Friday</span><span>History available</span></footer>
      </div>
    </section>

    <section className="landing-final" aria-labelledby="landing-final-title">
      <div><p className="landing-section-kicker">Start with the next task</p><h2 id="landing-final-title">Bring your work into focus.</h2></div>
      <div><p>Create an account to organize personal tasks and collaborate in shared workspaces.</p>
        <Link className="landing-button landing-button--light" to="/register">Create account <span aria-hidden="true">→</span></Link></div>
    </section>

    <footer className="landing-footer">
      <Link to="/" className="landing-footer__brand"><span aria-hidden="true">T</span><strong>TaskTracker</strong></Link>
      <p>Focused task management for personal and shared work.</p>
      <nav aria-label="Footer navigation"><Link to="/">Home</Link><Link to="/login">Sign in</Link><Link to="/register">Create account</Link></nav>
    </footer>
  </main>;
}

export default HomePage;
