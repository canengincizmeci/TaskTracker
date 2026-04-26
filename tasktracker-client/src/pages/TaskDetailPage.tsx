import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { getTaskById } from "../api/taskService";
import type { Task } from "../types/task";

function TaskDetailPage() {
  const { id } = useParams();
  const [task, setTask] = useState<Task | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadTask = async () => {
      try {
        if (!id) return;

        const data = await getTaskById(Number(id));
        setTask(data);
      } catch (error) {
        console.log(error);
      } finally {
        setLoading(false);
      }
    };

    loadTask();
  }, [id]);

  if (loading) return <div className="page public-page state-message">Loading request...</div>;

  if (!task) {
    return (
      <div className="page public-page state-message">
        <p>Task not found.</p>
      </div>
    );
  }

  return (
    <main className="page public-page">
      <section className="detail-template">
        <div className="detail-content">
          <div className="detail-kicker">
            <span>{task.category}</span>
            <span>{task.status}</span>
          </div>

          <h1>{task.title}</h1>

          <div className="description-card">
            <h2>Description</h2>
            <p>{task.description}</p>
          </div>
        </div>

        <aside className="detail-aside">
          <h2>Request details</h2>

          <div className="detail-info">
            <span>Priority</span>
            <strong>{task.priority}</strong>
          </div>

          <div className="detail-info">
            <span>Status</span>
            <strong>{task.status}</strong>
          </div>

          <div className="detail-info">
            <span>Category</span>
            <strong>{task.category}</strong>
          </div>

          <div className="detail-info">
            <span>Created</span>
            <strong>{new Date(task.createdAt).toLocaleDateString("tr-TR")}</strong>
          </div>
        </aside>
      </section>
    </main>
  );
}

export default TaskDetailPage;