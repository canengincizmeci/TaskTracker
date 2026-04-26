import { useEffect, useState } from "react";
import { getAllTasks } from "../api/taskService";
import type { Task } from "../types/task";
import TaskCard from "../components/TaskCard";

function HomePage() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const loadData = async () => {
      try {
        const data = await getAllTasks();
        setTasks(data);
      } catch (error) {
        console.log(error);
        setError("Tasks could not be loaded.");
      } finally {
        setLoading(false);
      }
    };

    loadData();
  }, []);

  const activeCount = tasks.filter((task) => task.status !== "Done").length;
  const highPriorityCount = tasks.filter(
    (task) => task.priority === "High" || task.priority === "Critical"
  ).length;
  const completedCount = tasks.filter((task) => task.status === "Done").length;

  if (loading) return <div className="page public-page state-message">Loading requests...</div>;
  if (error) return <div className="page public-page state-message">{error}</div>;

  return (
    <main className="page public-page">
      <section className="landing-hero">
        <div className="hero-content">
          <span className="product-pill">TaskTracker / Request Management</span>
          <h1>Track requests without losing operational context.</h1>
          <p>
            A lightweight public request board for service tasks, internal operations,
            support items and follow-up work.
          </p>
        </div>

        <div className="hero-panel">
          <div className="panel-header">
            <span>Today’s overview</span>
            <strong>{tasks.length} requests</strong>
          </div>

          <div className="metric-list">
            <div>
              <span>Active</span>
              <strong>{activeCount}</strong>
            </div>
            <div>
              <span>High priority</span>
              <strong>{highPriorityCount}</strong>
            </div>
            <div>
              <span>Completed</span>
              <strong>{completedCount}</strong>
            </div>
          </div>
        </div>
      </section>

      <section className="content-shell">
        <aside className="summary-sidebar">
          <h2>Board summary</h2>
          <p>
            Requests are listed by creation order. Open items stay visible until an
            administrator marks or removes them.
          </p>

          <div className="summary-box">
            <span>Total requests</span>
            <strong>{tasks.length}</strong>
          </div>

          <div className="summary-box">
            <span>Needs attention</span>
            <strong>{highPriorityCount}</strong>
          </div>
        </aside>

        <section className="request-list-area">
          <div className="request-list-header">
            <div>
              <h2>Latest requests</h2>
              <p>Operational tasks and service requests currently visible to users.</p>
            </div>
          </div>

          {tasks.length === 0 ? (
            <div className="empty-card">
              <h3>No requests yet</h3>
              <p>When new tasks are created, they will appear here.</p>
            </div>
          ) : (
            <div className="task-list">
              {tasks.map((task) => (
                <TaskCard key={task.id} task={task} />
              ))}
            </div>
          )}
        </section>
      </section>
    </main>
  );
}

export default HomePage;