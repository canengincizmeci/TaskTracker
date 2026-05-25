import { Link } from "react-router-dom";

function HomePage() {
  return (
    <main className="page public-page home-landing-page">
      <section className="home-hero">
        <div className="home-hero-content">
          <span className="product-pill">TaskTracker / Collaborative Task Management</span>

          <h1>Organize your tasks, share work, and keep ownership clear.</h1>

          <p>
            TaskTracker helps users create tasks, manage priorities, and share
            responsibilities with the right people without losing context.
          </p>

          <div className="home-hero-actions">
            <Link to="/register" className="primary-button home-action-button">
              Create account
            </Link>

            <Link to="/login" className="secondary-button home-action-button">
              Sign in
            </Link>
          </div>
        </div>

        <div className="home-preview-card">
          <div className="preview-card-header">
            <span>Workspace preview</span>
            <strong>Live structure</strong>
          </div>

          <div className="preview-task-card">
            <div>
              <span className="task-category">Product</span>
              <h3>Prepare task sharing flow</h3>
              <p>Owner can invite another user with selected permission.</p>
            </div>

            <span className="priority-pill priority-high">High</span>
          </div>

          <div className="preview-task-card">
            <div>
              <span className="task-category">Backend</span>
              <h3>Connect permissions to UI</h3>
              <p>Shared users can view or edit based on task access.</p>
            </div>

            <span className="priority-pill priority-medium">Medium</span>
          </div>

          <div className="preview-users">
            <span>Shared with</span>

            <div>
              <strong>CE</strong>
              <strong>AK</strong>
              <strong>MY</strong>
            </div>
          </div>
        </div>
      </section>

      <section className="home-section">
        <div className="section-heading">
          <span className="eyebrow">WHY TASKTRACKER</span>
          <h2>Built for real task ownership.</h2>
          <p>
            Not just a public board. TaskTracker is moving toward a user-based
            workspace where every task has an owner, visibility and sharing
            rules.
          </p>
        </div>

        <div className="feature-grid">
          <article className="feature-card">
            <span>01</span>
            <h3>User based tasks</h3>
            <p>
              Users can create their own tasks and keep personal work separated
              from shared work.
            </p>
          </article>

          <article className="feature-card">
            <span>02</span>
            <h3>Task sharing</h3>
            <p>
              Add people to a task and control who can access the details of
              that work item.
            </p>
          </article>

          <article className="feature-card">
            <span>03</span>
            <h3>Permission focused</h3>
            <p>
              The system is designed around ownership and access rules instead
              of simple open CRUD screens.
            </p>
          </article>
        </div>
      </section>

      <section className="home-split-section">
        <div>
          <span className="eyebrow">WORKFLOW</span>
          <h2>From personal task to shared responsibility.</h2>
          <p>
            Create a task, define its priority, invite another user when needed,
            and continue tracking the work from one place.
          </p>
        </div>

        <div className="workflow-list">
          <div>
            <strong>1</strong>
            <span>Create your task</span>
          </div>

          <div>
            <strong>2</strong>
            <span>Assign visibility and priority</span>
          </div>

          <div>
            <strong>3</strong>
            <span>Share with another user</span>
          </div>

          <div>
            <strong>4</strong>
            <span>Track progress from dashboard</span>
          </div>
        </div>
      </section>
    </main>
  );
}

export default HomePage;